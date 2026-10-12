using System.Text.RegularExpressions;

namespace ReactSqliteReplicasBlog.Features.Analytics;

public static partial class AnalyticsHelpers
{
    internal static int ClampRange(int? range) => range is 7 or 90 ? range.Value : 30;

    internal static (DateOnly From, DateOnly To) Window(int days)
    {
        var to = DateOnly.FromDateTime(DateTime.UtcNow);
        return (to.AddDays(-(days - 1)), to);
    }
}

/// <summary>Shared by the overview and per-post pages. postId == null means all of the user's posts.</summary>
public static partial class AnalyticsQueries
{
    private static string D(DateOnly d) => d.ToString("yyyy-MM-dd");

    /// <summary>rows = previous period followed by the current one (which starts at currentFrom).</summary>
    public static SummaryDto Summary(DayRow[] rows, DateOnly currentFrom, int posts)
    {
        var cf = D(currentFrom);
        var cur = rows.Where(r => r.Date.CompareTo(cf) >= 0).ToArray();
        var prev = rows.Where(r => r.Date.CompareTo(cf) < 0).ToArray();
        return new SummaryDto(
            cur.Sum(r => r.Views),
            cur.Sum(r => r.Likes),
            cur.Sum(r => r.Comments),
            posts,
            prev.Sum(r => r.Views),
            prev.Sum(r => r.Likes),
            prev.Sum(r => r.Comments)
        );
    }
}

/// <summary>
/// Records a deduplicated daily view (PostView, INSERT OR IGNORE) and bumps Post.ViewCount when the row is new.
/// Skips the post's author, bots and prefetches. Called by GET /posts/{id}.
/// Privacy: only a per-day salted SHA-256 of the visitor identity is stored, never the IP or user agent,
/// and hashes differ every day so a visitor cannot be linked across days.
/// </summary>
public sealed partial class ViewTracker
{
    [GeneratedRegex(
        "bot|crawl|spider|slurp|preview|monitor|curl|wget|python-requests",
        RegexOptions.IgnoreCase
    )]
    private static partial Regex BotRegex();
}
