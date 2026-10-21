using System.Security.Cryptography;
using System.Text.Json;
using Inkwell.Data;
using Inkwell.Features.Discovery;
using Inkwell.Jobs;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;

namespace Inkwell.Infrastructure;

public sealed record SendRecommendationDigest(Guid DeliveryId);

/// <summary>Queues opt-in local-time recommendation digests and tracks uncertain mail outcomes safely.</summary>
public sealed class DigestService(DiscoveryService discovery, DigestClock clock, IDataProtectionProvider protection)
{
    const int MaximumDueUsers = 500;
    const int MaximumPosts = 10;
    static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    const string UnsubscribePurpose = "Inkwell.DailyRecommendations.Unsubscribe.v1";

    public static void MapUnsubscribe(IEndpointRouteBuilder app) => app.MapGet("/email/unsubscribe/daily",
        async Task<IResult> (string? token, IDataProtectionProvider dataProtection, AppDb db, TimeProvider time, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(token)) return Results.BadRequest("Invalid unsubscribe link.");
            try
            {
                var payload = dataProtection.CreateProtector(UnsubscribePurpose).Unprotect(token).Split('|');
                if (payload.Length != 3 || !Guid.TryParse(payload[0], out var userId)
                    || !long.TryParse(payload[1], out var expires) || payload[2] != "daily-recommendation-digest"
                    || time.GetUtcNow().ToUnixTimeSeconds() > expires)
                    return Results.BadRequest("This unsubscribe link is invalid or expired.");
                var user = await db.Users.SingleOrDefaultAsync(row => row.Id == userId, ct);
                if (user is not null)
                {
                    user.Preferences.DailyRecommendationDigest = false;
                    user.NextDigestAtUtc = null;
                    await db.SaveChangesAsync(ct);
                }
                return Results.Content("<!doctype html><html><body><h1>You're unsubscribed</h1><p>Daily recommendation emails are turned off.</p></body></html>", "text/html");
            }
            catch (CryptographicException) { return Results.BadRequest("This unsubscribe link is invalid or expired."); }
        }).AllowAnonymous();

    public async Task<int> EnqueueDueAsync(AppDb db, JobQueue jobs, CancellationToken cancellationToken = default)
    {
        var now = clock.UtcNow.UtcDateTime;
        var users = await db.Users.Where(user => user.EmailConfirmed && user.Preferences.DailyRecommendationDigest
                && user.NextDigestAtUtc != null && user.NextDigestAtUtc <= now)
            .OrderBy(user => user.NextDigestAtUtc).ThenBy(user => user.Id).Take(MaximumDueUsers).ToListAsync(cancellationToken);
        var queued = 0;
        foreach (var user in users)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var zoneId = user.Preferences.TimeZoneId;
            if (!DigestClock.IsValidIanaTimeZone(zoneId))
            {
                user.NextDigestAtUtc = null;
                await db.SaveChangesAsync(cancellationToken);
                continue;
            }
            if (!clock.TryGetDueWindow(zoneId, out var window))
            {
                user.NextDigestAtUtc = DigestClock.NextDueUtc(zoneId, new DateTimeOffset(now, TimeSpan.Zero)).UtcDateTime;
                await db.SaveChangesAsync(cancellationToken);
                continue;
            }

            await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);
            var sendDate = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(new DateTimeOffset(now, TimeSpan.Zero), DigestClock.FindIanaTimeZone(zoneId)).DateTime);
            var exists = await db.DigestDeliveries.AnyAsync(row => row.UserId == user.Id && row.LocalDate == sendDate, cancellationToken);
            user.NextDigestAtUtc = DigestClock.NextDueUtc(zoneId, new DateTimeOffset(now, TimeSpan.Zero)).UtcDateTime;
            if (exists)
            {
                await db.SaveChangesAsync(cancellationToken);
                await tx.CommitAsync(cancellationToken);
                continue;
            }
            var candidates = await discovery.GetDigestCandidatesAsync(db, user.Id, window, new DateTimeOffset(now, TimeSpan.Zero), MaximumPosts, cancellationToken);
            if (candidates.Count == 0)
            {
                await db.SaveChangesAsync(cancellationToken);
                await tx.CommitAsync(cancellationToken);
                continue;
            }
            var zone = DigestClock.FindIanaTimeZone(zoneId);
            var delivery = new DigestDelivery
            {
                UserId = user.Id,
                // Uniqueness is the user's local send day; this window is the prior local content day.
                LocalDate = sendDate,
                TimeZoneId = zoneId,
                WindowStartUtc = window.StartUtc.UtcDateTime,
                WindowEndUtc = window.EndUtc.UtcDateTime,
                ExpiresAtUtc = DigestClock.GetLocalDayBoundaryUtc(sendDate.AddDays(1), zone).UtcDateTime,
                PostIdsJson = JsonSerializer.Serialize(candidates.Select(item => item.PostId).ToArray(), JsonOptions),
                Status = "Queued",
                CreatedAt = now,
            };
            db.DigestDeliveries.Add(delivery);
            await db.SaveChangesAsync(cancellationToken);
            jobs.Enqueue(db, new SendRecommendationDigest(delivery.Id));
            await tx.CommitAsync(cancellationToken);
            queued++;
        }
        if (queued > 0) jobs.Wake();
        return queued;
    }

    string UnsubscribeUrl(string baseUrl, Guid userId, DateTimeOffset now)
    {
        var expires = now.AddDays(180).ToUnixTimeSeconds();
        var token = protection.CreateProtector(UnsubscribePurpose).Protect($"{userId}|{expires}|daily-recommendation-digest");
        return $"{baseUrl.TrimEnd('/')}/email/unsubscribe/daily?token={Uri.EscapeDataString(token)}";
    }

    public async Task SendAsync(AppDb db, EmailSender email, Guid deliveryId, string baseUrl, string appName, CancellationToken cancellationToken = default)
    {
        if (deliveryId == Guid.Empty) throw new ArgumentOutOfRangeException(nameof(deliveryId));
        var delivery = await db.DigestDeliveries.AsNoTracking().SingleOrDefaultAsync(row => row.Id == deliveryId, cancellationToken);
        if (delivery is null || delivery.Status is "Sent" or "Failed" or "Unknown" or "Skipped") return;
        if (delivery.ExpiresAtUtc <= clock.UtcNow.UtcDateTime)
        {
            if (delivery.Status == "Sending") await SetUnknownAsync(db, deliveryId, "A prior send expired with an uncertain outcome; reconcile manually.");
            else await SetStatusAsync(db, deliveryId, "Skipped", "The intended local send day has ended.");
            return;
        }
        if (delivery.Status == "Sending")
        {
            await SetUnknownAsync(db, deliveryId, "Previous send outcome is unknown; reconcile manually before resending.");
            return;
        }

        var originalIds = JsonSerializer.Deserialize<Guid[]>(delivery.PostIdsJson, JsonOptions) ?? [];
        if (originalIds.Length == 0) { await SetStatusAsync(db, deliveryId, "Skipped", "No recommended posts were stored."); return; }
        var recipient = await db.Users.AsNoTracking().Where(user => user.Id == delivery.UserId)
            .Select(user => new { user.IsDemo, user.Email, user.DisplayName, user.EmailConfirmed,
                user.Preferences.TimeZoneId, user.Preferences.DailyRecommendationDigest })
            .SingleOrDefaultAsync(cancellationToken);
        if (recipient is null || !recipient.EmailConfirmed || !recipient.DailyRecommendationDigest)
        {
            await SetStatusAsync(db, deliveryId, "Skipped", "Recipient is no longer verified or opted in."); return;
        }
        if (!string.Equals(recipient.TimeZoneId, delivery.TimeZoneId, StringComparison.Ordinal))
        {
            await SetStatusAsync(db, deliveryId, "Skipped", "The recipient changed their time zone."); return;
        }
        if (recipient.IsDemo && !(recipient.Email?.StartsWith("inertia.demo42.", StringComparison.OrdinalIgnoreCase) == true
                && recipient.Email.EndsWith("@yopmail.com", StringComparison.OrdinalIgnoreCase)))
        {
            await SetStatusAsync(db, deliveryId, "Failed", "Demo delivery was blocked from the external mail provider."); return;
        }

        var window = new DigestWindow(delivery.LocalDate.AddDays(-1), new DateTimeOffset(DateTime.SpecifyKind(delivery.WindowStartUtc, DateTimeKind.Utc)),
            new DateTimeOffset(DateTime.SpecifyKind(delivery.WindowEndUtc, DateTimeKind.Utc)));
        var matching = await discovery.GetDigestCandidatesAsync(db, delivery.UserId, window, clock.UtcNow, 20, cancellationToken);
        var liveIds = matching.Select(item => item.PostId).ToHashSet();
        var selected = originalIds.Where(liveIds.Contains).Distinct().ToArray();
        var posts = selected.Length == 0 ? [] : await db.Posts.AsNoTracking()
            .Where(post => selected.Contains(post.Id) && !db.SeenPosts.Any(seen => seen.UserId == delivery.UserId && seen.PostId == post.Id))
            .Select(post => new { post.Id, post.Title, Excerpt = post.Body.Length > 260 ? post.Body.Substring(0, 260) : post.Body })
            .ToListAsync(cancellationToken);
        if (posts.Count == 0) { await SetStatusAsync(db, deliveryId, "Skipped", "All selected posts were seen or no longer match."); return; }

        var attempt = Guid.NewGuid().ToString("N");
        var claimed = await db.DigestDeliveries.Where(row => row.Id == deliveryId && row.Status == "Queued")
            .ExecuteUpdateAsync(set => set.SetProperty(row => row.Status, "Sending")
                .SetProperty(row => row.Attempts, row => row.Attempts + 1).SetProperty(row => row.ProviderReference, attempt)
                .SetProperty(row => row.LastError, (string?)null), cancellationToken);
        if (claimed == 0) return;
        var postRows = posts.OrderBy(post => Array.IndexOf(selected, post.Id)).Select(post =>
            (post.Title, post.Excerpt, $"{baseUrl.TrimEnd('/')}/posts/{post.Id}")).ToArray();
        var unsubscribe = UnsubscribeUrl(baseUrl, delivery.UserId, clock.UtcNow);
        var message = EmailTemplates.Recommendations(appName, recipient.DisplayName, postRows, unsubscribe);
        try
        {
            var providerReference = await email.SendAsync(recipient.Email!, recipient.DisplayName, message.Subject, message.Html, message.Plain, cancellationToken, "recommendations");
            await SetAttemptResultAsync(db, deliveryId, attempt, "Sent", null, providerReference);
        }
        catch (HttpRequestException error) when (error.StatusCode is { } status && (int)status is >= 400 and < 500)
        {
            await SetAttemptResultAsync(db, deliveryId, attempt, "Queued", $"Mail provider rejected the request with HTTP {(int)status}.", null);
            throw;
        }
        catch (InvalidOperationException)
        {
            await SetAttemptResultAsync(db, deliveryId, attempt, "Queued", "Mail provider is not configured.", null);
            throw;
        }
        catch (Exception)
        {
            await SetAttemptResultAsync(db, deliveryId, attempt, "Unknown", "Mail provider outcome is unknown; reconcile manually before resending.", null);
        }
    }

    static Task<int> SetUnknownAsync(AppDb db, Guid id, string reason) => db.DigestDeliveries.Where(row => row.Id == id && row.Status == "Sending")
        .ExecuteUpdateAsync(set => set.SetProperty(row => row.Status, "Unknown").SetProperty(row => row.LastError, reason));
    static Task<int> SetStatusAsync(AppDb db, Guid id, string status, string reason) => db.DigestDeliveries.Where(row => row.Id == id && row.Status == "Queued")
        .ExecuteUpdateAsync(set => set.SetProperty(row => row.Status, status).SetProperty(row => row.LastError, reason));
    static Task<int> SetAttemptResultAsync(AppDb db, Guid id, string token, string status, string? error, string? reference) =>
        db.DigestDeliveries.Where(row => row.Id == id && row.Status == "Sending" && row.ProviderReference == token)
            .ExecuteUpdateAsync(set => set.SetProperty(row => row.Status, status).SetProperty(row => row.ProviderReference, reference)
                .SetProperty(row => row.LastError, error));
}
