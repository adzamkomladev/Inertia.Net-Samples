using MongoDB.Driver;

namespace CarParker.Features.Lots;

/// <summary>A lot's standard grid: one section per level (A, B, ...) so the (lot, label) unique index holds, 12 spots per
/// section: handicap, EV, two compact, standard, then covered at the end.</summary>
public static class SpotGrid
{
    static readonly string[] Sections = ["A", "B", "C", "D"];

    public static List<Spot> For(Lot lot) =>
        [.. Enumerable.Range(1, Math.Min(lot.LevelCount, Sections.Length)).SelectMany(level => Enumerable.Range(1, 12).Select(number => new Spot
        {
            LotId = lot.Id,
            Label = $"{Sections[level - 1]}-{number:00}",
            Level = level,
            Type = number switch { 1 => SpotType.Handicap, 2 => SpotType.Ev, <= 4 => SpotType.Compact, >= 11 => SpotType.Covered, _ => SpotType.Standard },
        }))];
}

public static class LotsUtils
{
    // Demo data: the list shows today's free count, with weekends two spots hotter. Days are the lot's own calendar days.
    public static DayAvailability[] Availability(int total, int free, string? timeZoneId = null)
    {
        var today = LotTime.Today(DateTime.UtcNow, LotTime.Zone(timeZoneId));
        return Enumerable.Range(0, 14).Select(i =>
        {
            var day = today.AddDays(i);
            var pressure = day.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday ? 2 : 0;
            return new DayAvailability(day.ToString("yyyy-MM-dd"), Math.Max(0, free - pressure), total);
        }).ToArray();
    }

}

/// <summary>The lot's wall clock. Storage and the API stay UTC; drivers see and enter every time in the lot's zone.</summary>
public static class LotTime
{
    /// <summary>The operator's zone: new lots start on it, and views across all lots (the admin overview's "today", analytics
    /// over every lot) count days on it. Accra keeps GMT all year.</summary>
    public const string DefaultZoneId = "Africa/Accra";

    public static TimeZoneInfo DefaultZone => Zone(DefaultZoneId);

    /// <summary>The lot's zone, or UTC when the id is missing or unknown to this host.</summary>
    public static TimeZoneInfo Zone(string? id) =>
        id is { Length: > 0 } && TimeZoneInfo.TryFindSystemTimeZoneById(id, out var zone) ? zone : TimeZoneInfo.Utc;

    /// <summary>A lot-local date and time of day as a UTC instant. A time skipped by a spring-forward gap moves on by
    /// the gap's real length (02:30 on the US change day is 03:30; 02:15 on Lord Howe's 30-minute change is 02:45); a
    /// repeated fall-back time resolves to standard time, the later instant.</summary>
    public static DateTime ToUtc(DateOnly day, TimeOnly time, TimeZoneInfo zone)
    {
        var local = day.ToDateTime(time, DateTimeKind.Unspecified);
        if (zone.IsInvalidTime(local))
            local = local.Add(zone.GetUtcOffset(local.AddHours(12)) - zone.GetUtcOffset(local.AddHours(-12)));
        return TimeZoneInfo.ConvertTimeToUtc(local, zone);
    }

    public static DateTime ToLocal(DateTime utc, TimeZoneInfo zone) =>
        TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), zone);

    public static DateOnly Today(DateTime nowUtc, TimeZoneInfo zone) => DateOnly.FromDateTime(ToLocal(nowUtc, zone));

    /// <summary>Local midnight of <paramref name="day"/> as a UTC instant; days are 23 or 25 hours long across DST changes.</summary>
    public static DateTime StartOfDay(DateOnly day, TimeZoneInfo zone) => ToUtc(day, TimeOnly.MinValue, zone);

    /// <summary>"New York time" for America/New_York, "UTC" for UTC; Windows ids read as they are.</summary>
    public static string ZoneName(string? id)
    {
        if (string.IsNullOrWhiteSpace(id) || id is "UTC" or "Etc/UTC" or "Etc/GMT") return "UTC";
        var slash = id.LastIndexOf('/');
        return slash < 0 ? id : $"{id[(slash + 1)..].Replace('_', ' ')} time";
    }

    /// <summary>"Wed 18 Nov 2026, 14:00 New York time" for emails and receipts.</summary>
    public static string Format(DateTime utc, string? zoneId) =>
        ToLocal(utc, Zone(zoneId)).ToString("ddd d MMM yyyy, HH:mm", System.Globalization.CultureInfo.InvariantCulture) + " " + ZoneName(zoneId);

    /// <summary>"Wed 18 Nov 2026, 14:00 to 18:00 New York time"; the end repeats its date when it falls on another day.</summary>
    public static string Span(DateTime? start, DateTime? end, string? zoneId)
    {
        if (start is not { } s || end is not { } e) return "See booking";
        var zone = Zone(zoneId);
        var (from, to) = (ToLocal(s, zone), ToLocal(e, zone));
        var culture = System.Globalization.CultureInfo.InvariantCulture;
        var tail = from.Date == to.Date ? to.ToString("HH:mm", culture) : to.ToString("ddd d MMM yyyy, HH:mm", culture);
        return $"{from.ToString("ddd d MMM yyyy, HH:mm", culture)} to {tail} {ZoneName(zoneId)}";
    }
}

public static class LotHours
{
    public static string? Error(bool open24Hours, string opens, string closes, string timeZoneId)
    {
        if (string.IsNullOrWhiteSpace(timeZoneId) || !TimeZoneInfo.TryFindSystemTimeZoneById(timeZoneId, out _)) return "Enter a valid time zone.";
        if (!open24Hours && (!TimeOnly.TryParseExact(opens, "HH:mm", out var from) || !TimeOnly.TryParseExact(closes, "HH:mm", out var to) || from >= to))
            return "Opening must be earlier than closing; use 24-hour access for overnight stays.";
        return null;
    }
    public static bool Contains(Lot lot, DateTime start, DateTime end)
    {
        if (lot.Open24Hours) return end > start;
        if (Error(false, lot.Opens, lot.Closes, lot.TimeZoneId) is not null) return false;
        var zone = TimeZoneInfo.FindSystemTimeZoneById(lot.TimeZoneId);
        var from = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(start, DateTimeKind.Utc), zone);
        var to = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(end, DateTimeKind.Utc), zone);
        return end > start && from.Date == to.Date && TimeOnly.FromDateTime(from) >= TimeOnly.Parse(lot.Opens)
            && TimeOnly.FromDateTime(to) <= TimeOnly.Parse(lot.Closes);
    }
}
