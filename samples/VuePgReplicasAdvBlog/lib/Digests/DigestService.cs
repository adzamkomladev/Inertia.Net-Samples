using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using VuePgReplicasAdvBlog.Data;
using VuePgReplicasAdvBlog.Features.Discovery;
using VuePgReplicasAdvBlog.Features.Email;
using VuePgReplicasAdvBlog.Infrastructure;

namespace VuePgReplicasAdvBlog.Features.Digests;

/// <summary>The job carries only the recipient snapshot; post IDs stay in the durable delivery row.</summary>
public sealed record SendRecommendationDigest(Guid DeliveryId, string To, string Name) : IJob;

/// <summary>Schedules opt-in local-time digests and delivers each persisted daily selection at most once.</summary>
public sealed class DigestService(DiscoveryService discovery, DigestClock clock, IDataProtectionProvider dataProtection)
{
    const int MaximumDueUsers = 500;
    const int MaximumPosts = 10;
    static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    const string UnsubscribePurpose = "VuePgReplicasAdvBlog.DailyRecommendations.Unsubscribe.v1";

    /// <summary>Disables daily recommendations after validating a signed unsubscribe token.</summary>
    public static async Task UnsubscribeAsync(string? token, IDataProtectionProvider protection, AppDb db, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(token)) throw new FeatureException(FailureKind.InvalidInput, "Invalid unsubscribe link.");
        try
        {
            var payload = protection.CreateProtector(UnsubscribePurpose).Unprotect(token);
            var parts = payload.Split('|');
            if (parts.Length != 3
                || !Guid.TryParse(parts[0], out var userId) || userId == Guid.Empty
                || !long.TryParse(parts[1], out var expiresAt)
                || parts[2] != "daily-recommendation-digest"
                || DateTimeOffset.UtcNow.ToUnixTimeSeconds() > expiresAt)
                throw new FeatureException(FailureKind.InvalidInput, "This unsubscribe link is invalid or expired.");

            var user = await db.Users.SingleOrDefaultAsync(row => row.Id == userId, ct);
            if (user is not null)
            {
                user.Preferences.DailyRecommendationDigest = false;
                user.NextDigestAtUtc = null;
                await db.SaveChangesAsync(ct);
            }
            return;
        }
        catch (CryptographicException) { throw new FeatureException(FailureKind.InvalidInput, "This unsubscribe link is invalid or expired."); }
        catch (FormatException) { throw new FeatureException(FailureKind.InvalidInput, "This unsubscribe link is invalid or expired."); }
    }

    /// <summary>Called by the five-minute scheduler. Each delivery and queue row commit together.</summary>
    public async Task<int> EnqueueDueAsync(AppDb db, JobQueue jobs, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(jobs);
        var now = clock.UtcNow.UtcDateTime;
        var users = await db.Users.Where(user => user.EmailVerifiedAt != null
                && user.Preferences.DailyRecommendationDigest
                && user.NextDigestAtUtc != null && user.NextDigestAtUtc <= now)
            .OrderBy(user => user.NextDigestAtUtc).ThenBy(user => user.Id)
            .Take(MaximumDueUsers)
            .ToListAsync(cancellationToken);

        var queued = 0;
        foreach (var user in users)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var timeZoneId = user.Preferences.TimeZoneId;
            if (!DigestClock.IsValidIanaTimeZone(timeZoneId))
            {
                user.NextDigestAtUtc = null;
                await db.SaveChangesAsync(cancellationToken);
                continue;
            }

            if (!clock.TryGetDueWindow(timeZoneId, out var window))
            {
                user.NextDigestAtUtc = DigestClock.NextDueUtc(timeZoneId, new DateTimeOffset(now, TimeSpan.Zero)).UtcDateTime;
                await db.SaveChangesAsync(cancellationToken);
                continue;
            }

            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
            var localSendDate = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(
                new DateTimeOffset(now, TimeSpan.Zero), DigestClock.FindIanaTimeZone(timeZoneId)).DateTime);
            var existing = await db.DigestDeliveries.AnyAsync(delivery => delivery.UserId == user.Id
                && delivery.LocalDate == localSendDate, cancellationToken);
            user.NextDigestAtUtc = DigestClock.NextDueUtc(timeZoneId, new DateTimeOffset(now, TimeSpan.Zero)).UtcDateTime;
            if (existing)
            {
                await db.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                continue;
            }

            var candidates = await discovery.GetDigestCandidatesAsync(
                db, user.Id, window, new DateTimeOffset(now, TimeSpan.Zero), MaximumPosts, cancellationToken);
            if (candidates.Count == 0)
            {
                await db.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                continue;
            }

            var delivery = new DigestDelivery
            {
                UserId = user.Id,
                // The uniqueness key is the user's local send date; WindowStart/End are yesterday's content day.
                LocalDate = localSendDate,
                TimeZoneId = timeZoneId,
                WindowStartUtc = window.StartUtc.UtcDateTime,
                WindowEndUtc = window.EndUtc.UtcDateTime,
                ExpiresAtUtc = DigestClock.GetLocalDayBoundaryUtc(localSendDate.AddDays(1), DigestClock.FindIanaTimeZone(timeZoneId)).UtcDateTime,
                PostIdsJson = JsonSerializer.Serialize(candidates.Select(item => item.PostId).ToArray(), JsonOptions),
                Status = "Queued",
                CreatedAt = now,
            };
            db.DigestDeliveries.Add(delivery);
            await db.SaveChangesAsync(cancellationToken);
            await jobs.EnqueueAsync(db, new SendRecommendationDigest(delivery.Id, user.Email, user.Name), ct: cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            queued++;
        }

        return queued;
    }

    string UnsubscribeUrl(string baseUrl, Guid userId, DateTimeOffset nowUtc)
    {
        var expiresAt = nowUtc.AddDays(180).ToUnixTimeSeconds();
        var token = dataProtection.CreateProtector(UnsubscribePurpose)
            .Protect($"{userId}|{expiresAt}|daily-recommendation-digest");
        return $"{baseUrl.TrimEnd('/')}/email/unsubscribe/daily?token={Uri.EscapeDataString(token)}";
    }

    /// <summary>
    /// Sends only saved candidates that remain public, unseen, and personally recommended. A stale
    /// Sending row is made Unknown and never replayed, since the provider has no idempotency contract.
    /// </summary>
    public async Task SendAsync(
        AppDb db,
        Mailer mailer,
        Guid deliveryId,
        string to,
        string name,
        string baseUrl,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(mailer);
        if (deliveryId == Guid.Empty) throw new ArgumentOutOfRangeException(nameof(deliveryId));

        var delivery = await db.DigestDeliveries.AsNoTracking().SingleOrDefaultAsync(row => row.Id == deliveryId, cancellationToken);
        if (delivery is null || delivery.Status is "Sent" or "Failed" or "Unknown" or "Skipped") return;
        if (delivery.ExpiresAtUtc <= clock.UtcNow.UtcDateTime)
        {
            if (delivery.Status == "Sending")
                await SetUnknownAsync(db, deliveryId, "A prior send expired with an uncertain outcome; reconcile manually.");
            else await SetStatusAsync(db, deliveryId, "Skipped", "The intended local send day has ended.");
            return;
        }
        if (delivery.Status == "Sending")
        {
            await SetUnknownAsync(db, deliveryId, "Previous send outcome is unknown; reconcile manually before resending.");
            return;
        }

        Guid[] originalIds;
        try { originalIds = JsonSerializer.Deserialize<Guid[]>(delivery.PostIdsJson, JsonOptions) ?? []; }
        catch (JsonException)
        {
            await SetStatusAsync(db, deliveryId, "Failed", "Stored digest selection is invalid.");
            return;
        }
        if (originalIds.Length == 0)
        {
            await SetStatusAsync(db, deliveryId, "Skipped", "No recommended posts were stored.");
            return;
        }

        var recipient = await db.Users.AsNoTracking().Where(user => user.Id == delivery.UserId)
            .Select(user => new { user.IsDemo, user.Email, user.Name, user.Preferences.TimeZoneId, Verified = user.EmailVerifiedAt != null, user.Preferences.DailyRecommendationDigest })
            .SingleOrDefaultAsync(cancellationToken);
        if (recipient is null || !recipient.Verified || !recipient.DailyRecommendationDigest)
        {
            await SetStatusAsync(db, deliveryId, "Skipped", "Recipient is no longer verified or opted in.");
            return;
        }
        if (!string.Equals(recipient.TimeZoneId, delivery.TimeZoneId, StringComparison.Ordinal))
        {
            await SetStatusAsync(db, deliveryId, "Skipped", "The recipient changed their time zone.");
            return;
        }
        to = recipient.Email;
        name = recipient.Name;
        if (recipient.IsDemo && !(to.StartsWith("inertia.demo42.", StringComparison.OrdinalIgnoreCase)
            && to.EndsWith("@yopmail.com", StringComparison.OrdinalIgnoreCase)))
        {
            await SetStatusAsync(db, deliveryId, "Failed", "Demo delivery was blocked from the external mail provider.");
            return;
        }

        var window = new DigestWindow(
            delivery.LocalDate.AddDays(-1),
            new DateTimeOffset(DateTime.SpecifyKind(delivery.WindowStartUtc, DateTimeKind.Utc)),
            new DateTimeOffset(DateTime.SpecifyKind(delivery.WindowEndUtc, DateTimeKind.Utc)));
        var currentMatches = await discovery.GetDigestCandidatesAsync(
            db, delivery.UserId, window, clock.UtcNow, 20, cancellationToken);
        var currentIds = currentMatches.Select(item => item.PostId).ToHashSet();
        var selectedIds = originalIds.Where(currentIds.Contains).Distinct().ToArray();
        var posts = selectedIds.Length == 0
            ? []
            : await db.Posts.AsNoTracking()
                .Where(post => selectedIds.Contains(post.Id) && post.ArchivedAt == null
                    && !db.SeenPosts.Any(seen => seen.UserId == delivery.UserId && seen.PostId == post.Id))
                .Select(post => new { post.Id, post.Title, post.Excerpt, post.Slug })
                .ToListAsync(cancellationToken);
        if (posts.Count == 0)
        {
            await SetStatusAsync(db, deliveryId, "Skipped", "All selected posts were seen or no longer match.");
            return;
        }

        var attemptToken = Guid.NewGuid().ToString("N");
        var claimed = await db.DigestDeliveries.Where(row => row.Id == deliveryId && row.Status == "Queued")
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(row => row.Status, "Sending")
                .SetProperty(row => row.Attempts, row => row.Attempts + 1)
                .SetProperty(row => row.ProviderReference, attemptToken)
                .SetProperty(row => row.LastError, (string?)null), cancellationToken);
        if (claimed == 0) return;

        var orderedPosts = posts.OrderBy(post => Array.IndexOf(selectedIds, post.Id)).ToArray();
        var unsubscribe = UnsubscribeUrl(baseUrl, delivery.UserId, clock.UtcNow);
        var message = Emails.Recommendations(to, name, orderedPosts.Select(post =>
            (post.Title, post.Excerpt, $"{baseUrl.TrimEnd('/')}/posts/{post.Slug}")).ToArray(), unsubscribe);
        try
        {
            var providerReference = await mailer.SendAsync(message.To, message.Subject, message.Html, message.Text, cancellationToken);
            await SetAttemptResultAsync(db, deliveryId, attemptToken, "Sent", null, providerReference);
        }
        catch (HttpRequestException error) when (error.StatusCode is { } status && (int)status is >= 400 and < 500)
        {
            await SetAttemptResultAsync(db, deliveryId, attemptToken, "Queued", $"Mail provider rejected the request with HTTP {(int)status}.", null);
            throw;
        }
        catch (InvalidOperationException)
        {
            await SetAttemptResultAsync(db, deliveryId, attemptToken, "Queued", "Mail provider is not configured.", null);
            throw;
        }
        catch (Exception)
        {
            await SetAttemptResultAsync(db, deliveryId, attemptToken, "Unknown", "Mail provider outcome is unknown; reconcile manually before resending.", null);
        }
    }

    static Task<int> SetUnknownAsync(AppDb db, Guid deliveryId, string message) =>
        db.DigestDeliveries.Where(row => row.Id == deliveryId && row.Status == "Sending")
            .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.Status, "Unknown")
                .SetProperty(row => row.LastError, message));

    static Task<int> SetStatusAsync(AppDb db, Guid deliveryId, string status, string error) =>
        db.DigestDeliveries.Where(row => row.Id == deliveryId && row.Status == "Queued")
            .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.Status, status)
                .SetProperty(row => row.LastError, error));

    static Task<int> SetAttemptResultAsync(AppDb db, Guid deliveryId, string attemptToken, string status, string? error, string? providerReference) =>
        db.DigestDeliveries.Where(row => row.Id == deliveryId && row.Status == "Sending" && row.ProviderReference == attemptToken)
            .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.Status, status)
                .SetProperty(row => row.ProviderReference, providerReference)
                .SetProperty(row => row.LastError, error));
}
