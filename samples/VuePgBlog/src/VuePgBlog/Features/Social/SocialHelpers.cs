using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using static VuePgBlog.Features.SocialService;

namespace VuePgBlog.Features;

public static partial class SocialHelpers
{
    public static string Cursor(DateTime at, Guid id) =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes($"{at.Ticks}:{id}"));

    public static bool TryCursor(string? cursor, out Edge? edge)
    {
        edge = null;
        if (string.IsNullOrEmpty(cursor) || cursor == "start")
            return true;
        if (cursor.Length > 100)
            return false;
        try
        {
            var parts = Encoding.UTF8.GetString(Convert.FromBase64String(cursor)).Split(':');
            if (
                parts.Length != 2
                || !long.TryParse(
                    parts[0],
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out var ticks
                )
                || !Guid.TryParse(parts[1], out var id)
                || ticks < 0
                || ticks > DateTime.MaxValue.Ticks
                || id == Guid.Empty
            )
                return false;
            edge = new Edge(new DateTime(ticks, DateTimeKind.Utc), id);
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    internal static IResult Feed(
        List<PostSummary> items,
        string? cursor,
        string? next,
        string mode,
        string title
    ) =>
        Render(
            "Feed/Index",
            new InertiaProps
            {
                ["posts"] = Scroll(new CursorPage<PostSummary>(items, cursor, next), "data"),
                ["mode"] = mode,
                ["title"] = title,
                ["filters"] = new { q = "" },
            }
        );

    /// <summary>
    /// My posts, archives and bookmarks load their timeline through a deferred prop:
    /// the page shell renders immediately and the client fetches posts in a follow-up request,
    /// so Feed/Index can show skeleton rows exactly like the analytics groups do. The value is
    /// built by Scroll's loader, so the prop stays mergeable and scroll-aware for infinite scroll.
    /// The ranked feed keeps eager posts because its snapshot cursor must 400/410 on the page load.
    /// </summary>
    internal static IResult DeferredFeed(
        string mode,
        string title,
        string? cursor,
        Func<CancellationToken, Task<(List<PostSummary> Items, string? Next)>> timeline
    )
    {
        Func<CancellationToken, Task<CursorPage<PostSummary>>> loader = async ct =>
        {
            var (items, next) = await timeline(ct);
            return new CursorPage<PostSummary>(items, cursor, next);
        };
        return Render(
            "Feed/Index",
            new InertiaProps
            {
                ["posts"] = Scroll(loader, "data").Defer("timeline"),
                ["mode"] = mode,
                ["title"] = title,
                ["filters"] = new { q = "" },
            }
        );
    }

    [GeneratedRegex("^[a-z0-9]+(?:-[a-z0-9]+)*$")]
    private static partial Regex TopicPattern();

    public static string[] NormalizeTopics(string[]? topics) =>
        (topics ?? []).Select(t => (t ?? "").Trim().ToLowerInvariant()).Distinct().ToArray();

    public static IResult? TopicsError(string[]? topics)
    {
        var normalized = NormalizeTopics(topics);
        return
            normalized.Length > 5
            || normalized.Any(t => t.Length is 0 or > 32 || !TopicPattern().IsMatch(t))
            ? Back()
                .WithErrors(
                    new Dictionary<string, string>
                    {
                        ["topics"] =
                            "Use up to five topics of 1â€“32 lowercase letters, numbers or hyphens.",
                    }
                )
            : null;
    }
}
