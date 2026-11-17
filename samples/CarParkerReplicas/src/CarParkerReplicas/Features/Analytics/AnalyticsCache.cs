using Inertia.Net;

namespace CarParker.Features.Analytics;

/// <summary>
/// The shared policy for the admin analytics props. They stay fresh for a minute, then the last values are served
/// while one request refreshes them. Entries carry narrow tags so a write only drops what it can change:
/// <list type="bullet">
/// <item>KPIs read live reservations, payments, sessions and refunds: tagged per lot (and under the all-lots view), so a
/// booking, payment, cancellation or check-in in one lot leaves other lots' entries alone. Refund decisions touch every KPI view.</item>
/// <item>The refund mix reads refunds only, so only refund writes drop it.</item>
/// <item>The daily series and top lots read the cron rollup (and lot names), so only the rollup and lot edits drop them.</item>
/// </list>
/// </summary>
public static class AnalyticsCache
{
    public const string KpisTag = "analytics:kpis";
    public const string RefundsTag = "analytics:refunds";
    public const string RollupTag = "analytics:rollup";

    /// <summary>Tag of the KPI entries for one lot; Guid.Empty is the all-lots view.</summary>
    public static string KpisLotTag(Guid lotId) => $"analytics:kpis:{lotId}";

    public static readonly TimeSpan FreshFor = TimeSpan.FromSeconds(60);
    public static readonly TimeSpan StaleFor = TimeSpan.FromSeconds(30);

    /// <summary>Tags for a KPI entry: the broad one plus the one for its lot filter.</summary>
    public static string[] KpiTags(Guid lotId) => [KpisTag, KpisLotTag(lotId)];

    /// <summary>A reservation, payment, session or cancellation in <paramref name="lotId"/> changed: drop that lot's KPIs and the all-lots view.</summary>
    public static async Task LotActivityAsync(IInertiaCache cache, Guid lotId, CancellationToken ct)
    {
        await cache.RemoveByTagAsync(KpisLotTag(lotId), ct);
        await cache.RemoveByTagAsync(KpisLotTag(Guid.Empty), ct);
    }

    /// <summary>A refund was requested, decided or paid: the refund mix and every KPI view (pending and paid refund totals) change.</summary>
    public static async Task RefundsChangedAsync(IInertiaCache cache, CancellationToken ct)
    {
        await cache.RemoveByTagAsync(RefundsTag, ct);
        await cache.RemoveByTagAsync(KpisTag, ct);
    }

    /// <summary>The rollup was rebuilt or a lot was created or renamed: the series and top-lot charts change.</summary>
    public static async Task RollupChangedAsync(IInertiaCache cache, CancellationToken ct) =>
        await cache.RemoveByTagAsync(RollupTag, ct);
}
