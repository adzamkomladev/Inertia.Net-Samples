using System.Globalization;
using System.Text;

namespace Inkwell.Infrastructure;

public readonly record struct Edge(DateTime At, Guid Id);

public sealed record CursorPage<T>(List<T> Data, string? Cursor, string? NextCursor) : IProvidesScrollMetadata
{
    public ScrollMetadata GetScrollMetadata() => new("cursor", null, NextCursor, Cursor ?? "start");
}

/// <summary>Opaque keyset cursors over (timestamp, id), newest first.</summary>
public static class Paging
{
    public static string Encode(DateTime at, Guid id) => Convert.ToBase64String(Encoding.UTF8.GetBytes($"{at.Ticks}:{id}"));

    /// <summary>False for a malformed cursor. A missing or "start" cursor is valid and yields no edge.</summary>
    public static bool TryDecode(string? cursor, out Edge? edge)
    {
        edge = null;
        if (string.IsNullOrEmpty(cursor) || cursor == "start") return true;
        if (cursor.Length > 100) return false;
        try
        {
            var parts = Encoding.UTF8.GetString(Convert.FromBase64String(cursor)).Split(':');
            if (parts.Length != 2
                || !long.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var ticks)
                || ticks > DateTime.MaxValue.Ticks
                || !Guid.TryParseExact(parts[1], "D", out var id))
                return false;
            edge = new Edge(new DateTime(ticks, DateTimeKind.Utc), id);
            return true;
        }
        catch (FormatException) { return false; }
    }
}
