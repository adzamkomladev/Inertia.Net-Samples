using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using MongoDB.Driver;
using SvelteMongoBlog.Data;
using SvelteMongoBlog.Features.Discovery;

namespace SvelteMongoBlog.Infrastructure;

public sealed record SendRecommendationDigest(Guid DeliveryId, string To, string Name) : IJob;

/// <summary>Creates one durable local-time recommendation delivery and never blindly retries an uncertain send.</summary>
public sealed class DigestService(DiscoveryService discovery, DigestClock clock, IDataProtectionProvider protection)
{
    const string Purpose = "SvelteMongoBlog.DailyRecommendations.Unsubscribe.v1";
    static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static void MapUnsubscribe(IEndpointRouteBuilder app) => app.MapGet("/email/unsubscribe/daily", async Task<IResult> (string? token, IDataProtectionProvider dataProtection, AppDb db, CancellationToken ct) =>
    {
        if (string.IsNullOrWhiteSpace(token)) return Results.BadRequest("Invalid unsubscribe link.");
        try
        {
            var parts = dataProtection.CreateProtector(Purpose).Unprotect(token).Split('|');
            if (parts.Length != 3 || !Guid.TryParse(parts[0], out var id) || id == Guid.Empty || !long.TryParse(parts[1], out var expiry)
                || parts[2] != "daily-recommendation-digest" || DateTimeOffset.UtcNow.ToUnixTimeSeconds() > expiry) return Results.BadRequest("This unsubscribe link is invalid or expired.");
            await db.Users.UpdateOneAsync(x => x.Id == id, Builders<User>.Update.Set(x => x.Preferences.DailyRecommendationDigest, false).Set(x => x.NextDigestAtUtc, null), cancellationToken: ct);
            return Results.Content("<!doctype html><html><body><h1>You're unsubscribed</h1><p>Daily recommendation emails are turned off.</p></body></html>", "text/html");
        }
        catch (CryptographicException) { return Results.BadRequest("This unsubscribe link is invalid or expired."); }
        catch (FormatException) { return Results.BadRequest("This unsubscribe link is invalid or expired."); }
    }).AllowAnonymous();

    public async Task<int> EnqueueDueAsync(AppDb db, JobQueue jobs, CancellationToken ct = default)
    {
        var now = clock.UtcNow.UtcDateTime;
        var users = await db.Users.Find(x => x.EmailVerifiedAt != null && x.Preferences.DailyRecommendationDigest && x.NextDigestAtUtc != null && x.NextDigestAtUtc <= now)
            .SortBy(x => x.NextDigestAtUtc).ThenBy(x => x.Id).Limit(500).ToListAsync(ct);
        var queued = 0;
        foreach (var user in users)
        {
            ct.ThrowIfCancellationRequested();
            var zoneId = user.Preferences.TimeZoneId;
            if (!DigestClock.IsValidIanaTimeZone(zoneId))
            {
                await db.Users.UpdateOneAsync(x => x.Id == user.Id, Builders<User>.Update.Set(x => x.NextDigestAtUtc, null), cancellationToken: ct); continue;
            }
            if (!clock.TryGetDueWindow(zoneId, out var window))
            {
                await db.Users.UpdateOneAsync(x => x.Id == user.Id, Builders<User>.Update.Set(x => x.NextDigestAtUtc, DigestClock.NextDueUtc(zoneId, new DateTimeOffset(now, TimeSpan.Zero)).UtcDateTime), cancellationToken: ct); continue;
            }
            var localDate = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(new DateTimeOffset(now, TimeSpan.Zero), DigestClock.FindIanaTimeZone(zoneId)).DateTime);
            try
            {
                var created = await db.TransactionAsync(async session =>
                {
                    var claimed = await db.Users.UpdateOneAsync(session, x => x.Id == user.Id && x.NextDigestAtUtc <= now,
                        Builders<User>.Update.Set(x => x.NextDigestAtUtc, DigestClock.NextDueUtc(zoneId, new DateTimeOffset(now, TimeSpan.Zero)).UtcDateTime), cancellationToken: ct);
                    if (claimed.ModifiedCount == 0) return false;
                    if (await db.DigestDeliveries.Find(session, x => x.UserId == user.Id && x.LocalDate == localDate).AnyAsync(ct)) return false;
                    var candidates = await discovery.GetDigestCandidatesAsync(db, user.Id, window, new DateTimeOffset(now, TimeSpan.Zero), 5, ct);
                    if (candidates.Count == 0) return false;
                    var delivery = new DigestDelivery
                    {
                        UserId = user.Id, LocalDate = localDate, TimeZoneId = zoneId,
                        WindowStartUtc = window.StartUtc.UtcDateTime, WindowEndUtc = window.EndUtc.UtcDateTime,
                        ExpiresAtUtc = DigestClock.GetLocalDayBoundaryUtc(localDate.AddDays(1), DigestClock.FindIanaTimeZone(zoneId)).UtcDateTime,
                        PostIdsJson = JsonSerializer.Serialize(candidates.Select(x => x.PostId).ToArray(), JsonOptions), Status = "Queued", CreatedAt = now,
                    };
                    await db.DigestDeliveries.InsertOneAsync(session, delivery, cancellationToken: ct);
                    await jobs.EnqueueAsync(db, new SendRecommendationDigest(delivery.Id, user.Email, user.Name), session: session, ct: ct);
                    return true;
                }, ct);
                if (created) queued++;
            }
            catch (MongoWriteException e) when (e.WriteError?.Category == ServerErrorCategory.DuplicateKey) { /* competing scheduler won today's unique key */ }
        }
        if (queued > 0) jobs.Wake();
        return queued;
    }

    public async Task SendAsync(AppDb db, Mailer mailer, Guid deliveryId, string to, string name, string baseUrl, CancellationToken ct = default)
    {
        var delivery = await db.DigestDeliveries.Find(x => x.Id == deliveryId).FirstOrDefaultAsync(ct);
        if (delivery is null || delivery.Status is "Sent" or "Failed" or "Unknown" or "Skipped") return;
        if (delivery.ExpiresAtUtc <= clock.UtcNow.UtcDateTime)
        {
            await SetStatus(db, deliveryId, delivery.Status == "Sending" ? "Unknown" : "Skipped", "The intended local send day has ended; reconcile an expired send manually.", ct);
            return;
        }
        if (delivery.Status == "Sending") { await SetStatus(db, deliveryId, "Unknown", "Previous send outcome is uncertain; reconcile manually before resending.", ct); return; }
        Guid[] stored;
        try { stored = JsonSerializer.Deserialize<Guid[]>(delivery.PostIdsJson, JsonOptions) ?? []; }
        catch (JsonException) { await SetStatus(db, deliveryId, "Failed", "Stored digest selection is invalid.", ct); return; }
        if (stored.Length == 0) { await SetStatus(db, deliveryId, "Skipped", "No recommended posts were stored.", ct); return; }

        var recipient = await db.Users.Find(x => x.Id == delivery.UserId).FirstOrDefaultAsync(ct);
        if (recipient is null || recipient.EmailVerifiedAt is null || !recipient.Preferences.DailyRecommendationDigest) { await SetStatus(db, deliveryId, "Skipped", "Recipient is no longer verified or opted in.", ct); return; }
        if (recipient.Preferences.TimeZoneId != delivery.TimeZoneId) { await SetStatus(db, deliveryId, "Skipped", "Recipient changed their time zone.", ct); return; }
        to = recipient.Email; name = recipient.Name;
        if (recipient.IsDemo && !(to.StartsWith("inertia.demo42.", StringComparison.OrdinalIgnoreCase) && to.EndsWith("@yopmail.com", StringComparison.OrdinalIgnoreCase)))
        { await SetStatus(db, deliveryId, "Failed", "Demo delivery blocked from external mail provider.", ct); return; }

        var window = new DigestWindow(delivery.LocalDate.AddDays(-1), new DateTimeOffset(DateTime.SpecifyKind(delivery.WindowStartUtc, DateTimeKind.Utc)), new DateTimeOffset(DateTime.SpecifyKind(delivery.WindowEndUtc, DateTimeKind.Utc)));
        var matches = await discovery.GetDigestCandidatesAsync(db, delivery.UserId, window, clock.UtcNow, 20, ct);
        var current = matches.Select(x => x.PostId).ToHashSet();
        var selected = stored.Where(current.Contains).Distinct().ToArray();
        var posts = selected.Length == 0 ? [] : await db.Posts.Find(x => selected.Contains(x.Id) && x.ArchivedAt == null).ToListAsync(ct);
        var seen = selected.Length == 0 ? [] : await db.SeenPosts.Find(x => x.UserId == delivery.UserId && selected.Contains(x.PostId)).Project(x => x.PostId).ToListAsync(ct);
        posts = posts.Where(x => !seen.Contains(x.Id)).ToList();
        if (posts.Count == 0) { await SetStatus(db, deliveryId, "Skipped", "All selected posts were seen or no longer match.", ct); return; }

        var token = Guid.NewGuid().ToString("N");
        var claimed = await db.DigestDeliveries.UpdateOneAsync(x => x.Id == deliveryId && x.Status == "Queued" && x.ExpiresAtUtc > clock.UtcNow.UtcDateTime,
            Builders<DigestDelivery>.Update.Set(x => x.Status, "Sending").Set(x => x.ProviderReference, token).Set(x => x.LastError, null).Inc(x => x.Attempts, 1), cancellationToken: ct);
        if (claimed.ModifiedCount == 0) return;
        var ordered = posts.OrderBy(x => Array.IndexOf(selected, x.Id)).ToArray();
        var unsubscribe = UnsubscribeUrl(baseUrl, delivery.UserId, clock.UtcNow);
        var message = Emails.Recommendations(to, name, ordered.Select(x => (x.Title, x.Excerpt, $"{baseUrl.TrimEnd('/')}/posts/{x.Slug}")).ToArray(), unsubscribe);
        try
        {
            var providerReference = await mailer.SendAsync(message.To, message.Subject, message.Html, message.Text, ct);
            await SetAttempt(db, deliveryId, token, "Sent", null, providerReference);
        }
        catch (HttpRequestException error) when (error.StatusCode is { } status && (int)status is >= 400 and < 500)
        { await SetAttempt(db, deliveryId, token, "Queued", $"Mail provider rejected HTTP {(int)status}.", null); throw; }
        catch (InvalidOperationException)
        { await SetAttempt(db, deliveryId, token, "Queued", "Mail provider is not configured.", null); throw; }
        catch (Exception)
        { await SetAttempt(db, deliveryId, token, "Unknown", "Mail provider outcome is uncertain; reconcile manually before resending.", null); }
    }

    string UnsubscribeUrl(string baseUrl, Guid userId, DateTimeOffset now) => $"{baseUrl.TrimEnd('/')}/email/unsubscribe/daily?token={Uri.EscapeDataString(protection.CreateProtector(Purpose).Protect($"{userId}|{now.AddDays(180).ToUnixTimeSeconds()}|daily-recommendation-digest"))}";
    static Task SetStatus(AppDb db, Guid id, string status, string error, CancellationToken ct) => db.DigestDeliveries.UpdateOneAsync(x => x.Id == id && (x.Status == "Queued" || x.Status == "Sending"), Builders<DigestDelivery>.Update.Set(x => x.Status, status).Set(x => x.LastError, error), cancellationToken: ct);
    static Task SetAttempt(AppDb db, Guid id, string token, string status, string? error, string? reference) => db.DigestDeliveries.UpdateOneAsync(x => x.Id == id && x.Status == "Sending" && x.ProviderReference == token, Builders<DigestDelivery>.Update.Set(x => x.Status, status).Set(x => x.LastError, error).Set(x => x.ProviderReference, reference));
}
