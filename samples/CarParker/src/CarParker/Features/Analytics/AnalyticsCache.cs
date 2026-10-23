namespace CarParker.Features.Analytics;

/// <summary>
/// The shared policy for the admin analytics props. They stay fresh for a minute, then the last values are served
/// while one request refreshes them. Every admin mutation that changes a KPI (a booking, a session, a payment, a
/// refund, a lot or spot) removes the whole <see cref="Tag"/>, because the overview and per-lot pages draw from the
/// same rollups and an admin write is far rarer than a dashboard read.
/// </summary>
public static class AnalyticsCache
{
    public const string Tag = "analytics";

    public static readonly TimeSpan FreshFor = TimeSpan.FromSeconds(60);
    public static readonly TimeSpan StaleFor = TimeSpan.FromSeconds(30);
}
