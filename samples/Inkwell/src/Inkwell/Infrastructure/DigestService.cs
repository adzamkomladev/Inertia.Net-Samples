using System.Text.Json;
using Inkwell.Data;
using Inkwell.Features.Discovery;
using Inkwell.Jobs;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Inkwell.Infrastructure;

public sealed record SendRecommendationDigest(Guid DeliveryId) : Runwick.IQueueJob;

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
    IJobOutbox jobs,
    EmailSender email,
    TimeProvider time,
    EmailUnsubscribe unsubscribe,
    IOptions<AppOptions> app)
{
    const int MaximumDueUsers = 500;
    const int MaximumPosts = 10;
    static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    DateTime Now => time.GetUtcNow().UtcDateTime;

    /// <summary>
    /// Queues a digest for every opted-in user whose 08:00 has passed. Ranking runs outside any transaction; the
    /// delivery rows and their Runwick jobs then commit together in one short write, so a failed run is simply retried.
    /// </summary>
    public async Task<int> EnqueueDueAsync(CancellationToken ct)
    {
        var now = Now;
        var users = await db.Users
            .Where(u => u.EmailConfirmed && u.Preferences.DailyRecommendationDigest && u.NextDigestAtUtc != null && u.NextDigestAtUtc <= now)
            .OrderBy(u => u.NextDigestAtUtc).ThenBy(u => u.Id)
            .Take(MaximumDueUsers)
            .ToListAsync(ct);
        var queued = new List<Runwick.IQueueJob>();
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
            queued.Add(new SendRecommendationDigest(delivery.Id));
        }
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await db.SaveChangesAsync(ct);
        await jobs.StageManyAsync(db, queued, ct);
        await tx.CommitAsync(ct);
        return queued.Count;
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
            .Select(p => new { p.Id, p.Title, Body = p.Body.Length > 1200 ? p.Body.Substring(0, 1200) : p.Body })
            .ToListAsync(ct);
        if (posts.Count == 0) { await SetStatusAsync(deliveryId, "Skipped", "All selected posts were seen or no longer match."); return; }

        var attempt = Guid.NewGuid().ToString("N");
        var claimed = await db.DigestDeliveries.Where(d => d.Id == deliveryId && d.Status == "Queued")
            .ExecuteUpdateAsync(s => s.SetProperty(d => d.Status, "Sending").SetProperty(d => d.Attempts, d => d.Attempts + 1)
                .SetProperty(d => d.ProviderReference, attempt).SetProperty(d => d.LastError, (string?)null), ct);
        if (claimed == 0) return;

        var baseUrl = app.Value.BaseUrl.TrimEnd('/');
        var rows = posts.OrderBy(p => Array.IndexOf(selected, p.Id)).Select(p => (p.Title, Inkwell.Features.Posts.Search.Excerpt(p.Body), $"{baseUrl}/posts/{p.Id}")).ToArray();
        var message = EmailTemplates.Recommendations(app.Value.Name, recipient.DisplayName, rows, unsubscribe.Footer(delivery.UserId, EmailKind.Daily));
        try
        {
            var reference = await email.SendAsync(recipient.Email!, recipient.DisplayName, message, ct, "recommendations");
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

    async Task<int> SetUnknownAsync(Guid id, string reason) =>
        await db.DigestDeliveries.Where(d => d.Id == id && d.Status == "Sending")
            .ExecuteUpdateAsync(s => s.SetProperty(d => d.Status, "Unknown").SetProperty(d => d.LastError, reason));

    async Task<int> SetStatusAsync(Guid id, string status, string reason) =>
        await db.DigestDeliveries.Where(d => d.Id == id && d.Status == "Queued")
            .ExecuteUpdateAsync(s => s.SetProperty(d => d.Status, status).SetProperty(d => d.LastError, reason));

    async Task<int> SetResultAsync(Guid id, string attempt, string status, string? error, string? reference) =>
        await db.DigestDeliveries.Where(d => d.Id == id && d.Status == "Sending" && d.ProviderReference == attempt)
            .ExecuteUpdateAsync(s => s.SetProperty(d => d.Status, status).SetProperty(d => d.ProviderReference, reference).SetProperty(d => d.LastError, error));
}
