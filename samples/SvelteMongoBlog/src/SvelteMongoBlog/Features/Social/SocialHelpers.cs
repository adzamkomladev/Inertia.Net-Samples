using System.Globalization;
using System.Linq.Expressions;
using System.Text;
using System.Text.RegularExpressions;
using MongoDB.Driver;
using static SvelteMongoBlog.Features.SocialService;

namespace SvelteMongoBlog.Features;

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
            var p = Encoding.UTF8.GetString(Convert.FromBase64String(cursor)).Split(':');
            if (
                p.Length != 2
                || !long.TryParse(
                    p[0],
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out var ticks
                )
                || !Guid.TryParse(p[1], out var id)
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

    [GeneratedRegex("^[a-z0-9]+(?:-[a-z0-9]+)*$")]
    private static partial Regex TopicPattern();

    public static string[] NormalizeTopics(string[]? topics) =>
        (topics ?? []).Select(t => (t ?? "").Trim().ToLowerInvariant()).Distinct().ToArray();
}

public static partial class SocialService
{
    internal static FilterDefinition<T> After<T>(
        Expression<Func<T, DateTime>> at,
        Expression<Func<T, Guid>> id,
        Edge? after
    )
    {
        if (after is not { } edge)
            return Builders<T>.Filter.Empty;
        var filter = Builders<T>.Filter;
        return filter.Lt(at, edge.At) | (filter.Eq(at, edge.At) & filter.Lt(id, edge.Id));
    }
}
