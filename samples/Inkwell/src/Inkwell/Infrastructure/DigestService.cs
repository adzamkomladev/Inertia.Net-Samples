using System.Security.Cryptography;
using System.Text.Json;
using Inkwell.Data;
using Inkwell.Features.Discovery;
using Inkwell.Jobs;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Inkwell.Infrastructure;

public sealed record SendRecommendationDigest(Guid DeliveryId);

/// <summary>The local calendar day a digest covers, as UTC instants.</summary>
public readonly record struct DigestWindow(DateOnly LocalDate, DateTime StartUtc, DateTime EndUtc);

/// <summary>Time-zone math for the daily digest: it goes out at 08:00 local time and covers the previous local day.</summary>
public static class DigestSchedule
{
    static readonly TimeOnly SendTime = new(8, 0);

    public static bool IsValidZone(string? id) =>
        !string.IsNullOrWhiteSpace(id)
        && TimeZoneInfo.TryFindSystemTimeZoneById(id, out var zone)
        && (zone.HasIanaId || string.Equals(id, "UTC", StringComparison.OrdinalIgnoreCase));

    /// <summary>Yesterday's local-day window, once the user's local time has reached 08:00.</summary>
    public static bool TryGetDueWindow(DateTime nowUtc, string zoneId, out DigestWindow window)
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById(zoneId);
        var local = TimeZoneInfo.ConvertTimeFromUtc(nowUtc, zone);
        if (TimeOnly.FromDateTime(local) < SendTime)
        {
            window = default;
            return false;
        }
        var today = DateOnly.FromDateTime(local);
        window = new DigestWindow(today.AddDays(-1), MidnightUtc(today.AddDays(-1), zoneId), MidnightUtc(today, zoneId));
        return true;
    }

    /// <summary>The next 08:00 local time after <paramref name="nowUtc"/>.</summary>
    public static DateTime NextDueUtc(string zoneId, DateTime nowUtc)
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById(zoneId);
        var local = TimeZoneInfo.ConvertTimeFromUtc(nowUtc, zone);
        var day = DateOnly.FromDateTime(local);
        if (TimeOnly.FromDateTime(local) >= SendTime) day = day.AddDays(1);
        return ToUtc(day.ToDateTime(SendTime), zone);
    }

    /// <summary>Start of the local day in UTC. Adjacent days share the same boundary, so windows stay contiguous across DST changes.</summary>
    public static DateTime MidnightUtc(DateOnly day, string zoneId) =>
        ToUtc(day.ToDateTime(TimeOnly.MinValue), TimeZoneInfo.FindSystemTimeZoneById(zoneId));

    static DateTime ToUtc(DateTime local, TimeZoneInfo zone)
    {
        if (zone.IsInvalidTime(local)) local = local.AddHours(1); // inside a DST gap: the clock skipped forward
        if (zone.IsAmbiguousTime(local)) // repeated hour: take the first occurrence
            return DateTime.SpecifyKind(local - zone.GetAmbiguousTimeOffsets(local).Max(), DateTimeKind.Utc);
        return TimeZoneInfo.ConvertTimeToUtc(local, zone);
    }
}

/// <summary>Queues opt-in daily recommendation digests and sends them at most once, treating an uncertain mail outcome as unknown.</summary>
public sealed class DigestService(
    AppDb db,
    DiscoveryService discovery,
    JobQueue jobs,
    EmailSender email,
    TimeProvider time,
    IDataProtectionProvider protection,
    IOptions<AppOptions> app)
{
    const int MaximumDueUsers = 500;
    const int MaximumPosts = 10;
    const string UnsubscribePurpose = "Inkwell.DailyRecommendations.Unsubscribe.v1";
    static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    DateTime Now => time.GetUtcNow().UtcDateTime;

    /// <summary>Validates the signed unsubscribe token and turns the daily digest off.</summary>
    public async Task<bool> UnsubscribeAsync(string? token, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(token)) return false;
        try
        {
            var payload = protection.CreateProtector(UnsubscribePurpose).Unprotect(token).Split('|');
            if (payload.Length != 3 || !Guid.TryParse(payload[0], out var userId)
                || !long.TryParse(payload[1], out var expires) || payload[2] != "daily-recommendation-digest"
                || time.GetUtcNow().ToUnixTimeSeconds() > expires)
                return false;
            var user = await db.Users.SingleOrDefaultAsync(u => u.Id == userId, ct);
            if (user is null) return true;
            user.Preferences.DailyRecommendationDigest = false;
            user.NextDigestAtUtc = null;
            await db.SaveChangesAsync(ct);
            return true;
        }
        catch (CryptographicException) { return false; }
    }

    /// <summary>Queues a digest for every opted-in user whose 08:00 has passed. One SaveChanges, so a failed run is simply retried.</summary>
    public async Task<int> EnqueueDueAsync(CancellationToken ct)
    {
        var now = Now;
        var users = await db.Users
            .Where(u => u.EmailConfirmed && u.Preferences.DailyRecommendationDigest && u.NextDigestAtUtc != null && u.NextDigestAtUtc <= now)
            .OrderBy(u => u.NextDigestAtUtc).ThenBy(u => u.Id)
            .Take(MaximumDueUsers)
            .ToListAsync(ct);
        var queued = 0;
        foreach (var user in users)
        {
            var zoneId = user.Preferences.TimeZoneId;
            if (!DigestSchedule.IsValidZone(zoneId))
            {
                user.NextDigestAtUtc = null;
                continue;
            }
            user.NextDigestAtUtc = DigestSchedule.NextDueUtc(zoneId, now);
            if (!DigestSchedule.TryGetDueWindow(now, zoneId, out var window)) continue;
            var sendDate = window.LocalDate.AddDays(1);
            if (await db.DigestDeliveries.AnyAsync(d => d.UserId == user.Id && d.LocalDate == sendDate, ct)) continue;
            var posts = await discovery.GetDigestCandidatesAsync(user.Id, window.StartUtc, window.EndUtc, MaximumPosts, ct);
            if (posts.Count == 0) continue;

            var delivery = new DigestDelivery
            {
                UserId = user.Id,
                LocalDate = sendDate, // one digest per user and local send day
                TimeZoneId = zoneId,
                WindowStartUtc = window.StartUtc,
                WindowEndUtc = window.EndUtc,
                ExpiresAtUtc = DigestSchedule.MidnightUtc(sendDate.AddDays(1), zoneId),
                PostIdsJson = JsonSerializer.Serialize(posts.Select(p => p.PostId), Json),
                CreatedAt = now,
            };
            db.DigestDeliveries.Add(delivery);
            jobs.Enqueue(db, new SendRecommendationDigest(delivery.Id));
            queued++;
        }
        await db.SaveChangesAsync(ct);
        if (queued > 0) jobs.Wake();
        return queued;
    }

    public async Task SendAsync(Guid deliveryId, CancellationToken ct)
    {
        var delivery = await db.DigestDeliveries.AsNoTracking().SingleOrDefaultAsync(d => d.Id == deliveryId, ct);
        if (delivery is null || delivery.Status is "Sent" or "Failed" or "Unknown" or "Skipped") return;
        if (delivery.Status == "Sending" || delivery.ExpiresAtUtc <= Now)
        {
            // A "Sending" row means an earlier attempt may or may not have reached the provider: never resend blindly.
            if (delivery.Status == "Sending") await SetUnknownAsync(deliveryId, "Previous send outcome is unknown; reconcile manually before resending.");
            else await SetStatusAsync(deliveryId, "Skipped", "The intended local send day has ended.");
            return;
        }

        var recipient = await db.Users.AsNoTracking().Where(u => u.Id == delivery.UserId)
            .Select(u => new { u.IsDemo, u.Email, u.DisplayName, u.EmailConfirmed, u.Preferences.TimeZoneId, u.Preferences.DailyRecommendationDigest })
            .SingleOrDefaultAsync(ct);
        string? skip = recipient is null || !recipient.EmailConfirmed || !recipient.DailyRecommendationDigest ? "Recipient is no longer verified or opted in."
            : recipient.TimeZoneId != delivery.TimeZoneId ? "The recipient changed their time zone."
            : null;
        if (skip is not null) { await SetStatusAsync(deliveryId, "Skipped", skip); return; }
        if (recipient!.IsDemo && !(recipient.Email!.StartsWith("inertia.demo42.", StringComparison.OrdinalIgnoreCase)
                && recipient.Email.EndsWith("@yopmail.com", StringComparison.OrdinalIgnoreCase)))
        {
            await SetStatusAsync(deliveryId, "Failed", "Demo delivery was blocked from the external mail provider.");
            return;
        }

        // Re-rank at send time: keep only stored posts that still match and have not been seen since.
        var stored = JsonSerializer.Deserialize<Guid[]>(delivery.PostIdsJson, Json) ?? [];
        var matching = (await discovery.GetDigestCandidatesAsync(delivery.UserId, delivery.WindowStartUtc, delivery.WindowEndUtc, 20, ct))
            .Select(i => i.PostId).ToHashSet();
        var selected = stored.Where(matching.Contains).Distinct().ToArray();
        var posts = selected.Length == 0 ? [] : await db.Posts.AsNoTracking()
            .Where(p => selected.Contains(p.Id) && !db.SeenPosts.Any(s => s.UserId == delivery.UserId && s.PostId == p.Id))
            .Select(p => new { p.Id, p.Title, Excerpt = p.Body.Length > 260 ? p.Body.Substring(0, 260) : p.Body })
            .ToListAsync(ct);
        if (posts.Count == 0) { await SetStatusAsync(deliveryId, "Skipped", "All selected posts were seen or no longer match."); return; }

        var attempt = Guid.NewGuid().ToString("N");
        var claimed = await db.DigestDeliveries.Where(d => d.Id == deliveryId && d.Status == "Queued")
            .ExecuteUpdateAsync(s => s.SetProperty(d => d.Status, "Sending").SetProperty(d => d.Attempts, d => d.Attempts + 1)
                .SetProperty(d => d.ProviderReference, attempt).SetProperty(d => d.LastError, (string?)null), ct);
        if (claimed == 0) return;

        var baseUrl = app.Value.BaseUrl.TrimEnd('/');
        var rows = posts.OrderBy(p => Array.IndexOf(selected, p.Id)).Select(p => (p.Title, p.Excerpt, $"{baseUrl}/posts/{p.Id}")).ToArray();
        var message = EmailTemplates.Recommendations(app.Value.Name, recipient.DisplayName, rows, UnsubscribeUrl(baseUrl, delivery.UserId));
        try
        {
            var reference = await email.SendAsync(recipient.Email!, recipient.DisplayName, message.Subject, message.Html, message.Plain, ct, "recommendations");
            await SetResultAsync(deliveryId, attempt, "Sent", null, reference);
        }
        catch (HttpRequestException e) when (e.StatusCode is { } status && (int)status is >= 400 and < 500)
        {
            await SetResultAsync(deliveryId, attempt, "Queued", $"Mail provider rejected the request with HTTP {(int)status}.", null);
            throw;
        }
        catch (InvalidOperationException)
        {
            await SetResultAsync(deliveryId, attempt, "Queued", "Mail provider is not configured.", null);
            throw;
        }
        catch (Exception)
        {
            await SetResultAsync(deliveryId, attempt, "Unknown", "Mail provider outcome is unknown; reconcile manually before resending.", null);
        }
    }

    string UnsubscribeUrl(string baseUrl, Guid userId)
    {
        var expires = time.GetUtcNow().AddDays(180).ToUnixTimeSeconds();
        var token = protection.CreateProtector(UnsubscribePurpose).Protect($"{userId}|{expires}|daily-recommendation-digest");
        return $"{baseUrl}/email/unsubscribe/daily?token={Uri.EscapeDataString(token)}";
    }

    Task<int> SetUnknownAsync(Guid id, string reason) =>
        db.DigestDeliveries.Where(d => d.Id == id && d.Status == "Sending")
            .ExecuteUpdateAsync(s => s.SetProperty(d => d.Status, "Unknown").SetProperty(d => d.LastError, reason));

    Task<int> SetStatusAsync(Guid id, string status, string reason) =>
        db.DigestDeliveries.Where(d => d.Id == id && d.Status == "Queued")
            .ExecuteUpdateAsync(s => s.SetProperty(d => d.Status, status).SetProperty(d => d.LastError, reason));

    Task<int> SetResultAsync(Guid id, string attempt, string status, string? error, string? reference) =>
        db.DigestDeliveries.Where(d => d.Id == id && d.Status == "Sending" && d.ProviderReference == attempt)
            .ExecuteUpdateAsync(s => s.SetProperty(d => d.Status, status).SetProperty(d => d.ProviderReference, reference).SetProperty(d => d.LastError, error));
}
