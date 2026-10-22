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

public static class SpotExtensions
{
    public static FilterDefinition<Spot> ByLot(this FilterDefinitionBuilder<Spot> _, Guid lotId) =>
        Builders<Spot>.Filter.Eq(x => x.LotId, lotId);
}

public static class LotsUtils
{
    // Demo data: the strip shows today's free count, with weekends two spots hotter.
    public static DayAvailability[] Availability(int total, int free)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        return Enumerable.Range(0, 14).Select(i =>
        {
            var day = today.AddDays(i);
            var pressure = day.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday ? 2 : 0;
            return new DayAvailability(day.ToString("yyyy-MM-dd"), Math.Max(0, free - pressure), total);
        }).ToArray();
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
