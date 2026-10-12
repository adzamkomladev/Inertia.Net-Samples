using System.Globalization;
using System.Text;

namespace VuePgReplicasAdvBlog.Infrastructure;

public readonly record struct CursorEdge(DateTime At, Guid Id);

public static class CursorHelpers
{
    public static string Cursor(DateTime at, Guid id) =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes($"{at.Ticks}:{id}"));

    public static bool TryCursor(string? cursor, out CursorEdge? edge)
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
            edge = new CursorEdge(new DateTime(ticks, DateTimeKind.Utc), id);
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }

}
