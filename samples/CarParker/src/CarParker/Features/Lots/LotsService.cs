using Inertia.Net;
using MongoDB.Driver;
using CarParker.Data;
using CarParker.Features.Bookings;

namespace CarParker.Features.Lots;

public sealed record LotCard(
    Guid Id, string Name, string Address, string Area, int LevelCount, long RatePerHourCents, long DailyMaxCents,
    string? ContactPhone, int Spots, int Free, double OccupancyPct, string[] SpotTypes, bool InsuranceCovered, long CoverageCents);

public sealed record SpotView(Guid Id, string Label, string Type, int Level, string State, DateTime? LastCheckpointAt, string? Note);

public sealed record Tariff(long RatePerHourCents, long DailyMaxCents, int InsurancePercent, long InsuranceMinCents, long CoverageCents);

public sealed record DayAvailability(string Date, int Free, int Total);

public sealed record LotOption(Guid Id, string Name);

public sealed record LotsPage(LotCard[] Data, int Page, bool HasMore) : IProvidesScrollMetadata
{
    public ScrollMetadata GetScrollMetadata() => ScrollMetadata.FromPage(Page, HasMore);
}

public sealed class LotsService(AppDb db)
{
    const int PageSize = 12;

    public async Task<LotCard[]> CardsAsync(IReadOnlyCollection<Lot> lots, CancellationToken ct)
    {
        if (lots.Count == 0) return [];
        var ids = lots.Select(l => l.Id).ToArray();
        var groups = await db.Spots.Aggregate()
            .Match(s => ids.Contains(s.LotId) && s.Active)
            .Group(s => new { s.LotId, s.Type, s.State }, g => new { g.Key.LotId, g.Key.Type, g.Key.State, Count = g.Count() })
            .ToListAsync(ct);
        var byLot = groups.ToLookup(g => g.LotId);
        return lots.Select(lot =>
        {
            var own = byLot[lot.Id].ToList();
            var spots = own.Sum(g => g.Count);
            var free = own.Where(g => g.State == SpotState.Free).Sum(g => g.Count);
            return new LotCard(lot.Id, lot.Name, lot.Address, lot.Area, lot.LevelCount, lot.RatePerHourCents, lot.DailyMaxCents,
                lot.ContactPhone, spots, free, spots == 0 ? 0 : Math.Round((spots - free) * 100.0 / spots, 1),
                own.Select(g => g.Type.ToString().ToLowerInvariant()).Distinct().Order().ToArray(), lot.InsuranceCovered, lot.CoverageCents);
        }).ToArray();
    }

    public Task<List<Lot>> ActiveAsync(CancellationToken ct) =>
        db.Lots.Find(l => l.IsActive).SortBy(l => l.Name).ToListAsync(ct);

    public async Task<Lot?> FindActiveAsync(Guid id, CancellationToken ct) =>
        await db.Lots.Find(l => l.Id == id && l.IsActive).FirstOrDefaultAsync(ct);

    public static List<LotOption> OptionsFor(IEnumerable<Lot> lots) => lots.Select(l => new LotOption(l.Id, l.Name)).ToList();

    // ponytail: matching lots are sorted and paged in memory, fine for a catalogue of hundreds; push it into the query beyond that.
    public async Task<LotsPage> QueryPageAsync(string? q, string? type, long? maxPrice, string? sort, int page, CancellationToken ct)
    {
        var filter = Builders<Lot>.Filter.Eq(l => l.IsActive, true);
        if (!string.IsNullOrWhiteSpace(q)) filter &= Builders<Lot>.Filter.Text(q.Trim());
        if (maxPrice is { } cap) filter &= Builders<Lot>.Filter.Lte(l => l.RatePerHourCents, cap);
        if (Enum.TryParse<SpotType>(type, ignoreCase: true, out var wanted))
            filter &= Builders<Lot>.Filter.In(l => l.Id, await db.Spots.Distinct(s => s.LotId, s => s.Active && s.Type == wanted).ToListAsync(ct));

        var lots = await db.Lots.Find(filter).ToListAsync(ct);
        var byName = StringComparer.OrdinalIgnoreCase;
        if (sort == "occupancy")
        {
            var all = (await CardsAsync(lots, ct)).OrderByDescending(c => c.OccupancyPct).ThenBy(c => c.Name, byName).ToArray();
            return new LotsPage(all.Skip((page - 1) * PageSize).Take(PageSize).ToArray(), page, all.Length > page * PageSize);
        }

        var ordered = sort switch
        {
            "price" => lots.OrderBy(l => l.RatePerHourCents).ThenBy(l => l.Name, byName),
            "price_desc" => lots.OrderByDescending(l => l.RatePerHourCents).ThenBy(l => l.Name, byName),
            _ => lots.OrderBy(l => l.Name, byName),
        };
        var cards = await CardsAsync(ordered.Skip((page - 1) * PageSize).Take(PageSize).ToArray(), ct);
        return new LotsPage(cards, page, lots.Count > page * PageSize);
    }

    /// <summary>Live spot board (lot page and guard console), level-first.</summary>
    public async Task<SpotView[]> SpotsAsync(Guid lotId, CancellationToken ct) =>
        (await db.Spots.Find(s => s.LotId == lotId && s.Active).SortBy(s => s.Level).ThenBy(s => s.Label).ToListAsync(ct))
            .Select(s => new SpotView(s.Id, s.Label, s.Type.ToString().ToLowerInvariant(), s.Level,
                s.State.ToString().ToLowerInvariant(), s.LastCheckpointAt, s.CheckpointNote)).ToArray();

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

    public static Tariff TariffOf(Lot lot) => new(
        lot.RatePerHourCents, lot.DailyMaxCents, PricingRules.InsurancePercent, PricingRules.InsurancePremiumMinCents, lot.CoverageCents);
}
