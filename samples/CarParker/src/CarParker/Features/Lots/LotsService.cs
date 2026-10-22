using Inertia.Net;
using MongoDB.Driver;
using CarParker.Data;
using CarParker.Features.Bookings;

namespace CarParker.Features.Lots;

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

    public async Task<List<Lot>> ActiveAsync(CancellationToken ct) =>
        await db.Lots.Find(l => l.IsActive).SortBy(l => l.Name).ToListAsync(ct);

    public async Task<Lot?> FindActiveAsync(Guid id, CancellationToken ct) =>
        await db.Lots.Find(l => l.Id == id && l.IsActive).FirstOrDefaultAsync(ct);

    public async Task<LotsPage> QueryPageAsync(string? q, string? type, long? maxPrice, string? sort, int page, CancellationToken ct)
    {
        var filter = Builders<Lot>.Filter.Eq(l => l.IsActive, true);
        if (!string.IsNullOrWhiteSpace(q)) filter &= Builders<Lot>.Filter.Text(q.Trim());
        if (maxPrice is { } cap) filter &= Builders<Lot>.Filter.Lte(l => l.RatePerHourCents, cap);
        if (Enum.TryParse<SpotType>(type, ignoreCase: true, out var wanted))
            filter &= Builders<Lot>.Filter.In(l => l.Id, await db.Spots.Distinct(s => s.LotId, s => s.Active && s.Type == wanted).ToListAsync(ct));

        if (sort == "occupancy")
        {
            // shortcut: occupancy ranking loads filtered lots; use a Mongo aggregation when the catalogue grows.
            var lots = await db.Lots.Find(filter).ToListAsync(ct);
            var byName = StringComparer.OrdinalIgnoreCase;
            var all = (await CardsAsync(lots, ct)).OrderByDescending(c => c.OccupancyPct).ThenBy(c => c.Name, byName).ToArray();
            return new LotsPage(all.Skip((page - 1) * PageSize).Take(PageSize).ToArray(), page, all.Length > page * PageSize);
        }

        var order = sort switch
        {
            "price" => Builders<Lot>.Sort.Ascending(l => l.RatePerHourCents).Ascending(l => l.Name),
            "price_desc" => Builders<Lot>.Sort.Descending(l => l.RatePerHourCents).Ascending(l => l.Name),
            _ => Builders<Lot>.Sort.Ascending(l => l.Name),
        };
        var pageLots = await db.Lots.Find(filter).Sort(order.Ascending(l => l.Id))
            .Skip((page - 1) * PageSize).Limit(PageSize + 1).ToListAsync(ct);
        var cards = await CardsAsync(pageLots.Take(PageSize).ToArray(), ct);
        return new LotsPage(cards, page, pageLots.Count > PageSize);
    }

    /// <summary>Live spot board (lot page and guard console), level-first.</summary>
    public async Task<SpotView[]> SpotsAsync(Guid lotId, CancellationToken ct) =>
        (await db.Spots.Find(s => s.LotId == lotId && s.Active).SortBy(s => s.Level).ThenBy(s => s.Label).ToListAsync(ct))
            .Select(s => new SpotView(s.Id, s.Label, s.Type.ToString().ToLowerInvariant(), s.Level,
                s.State.ToString().ToLowerInvariant(), s.LastCheckpointAt, s.CheckpointNote)).ToArray();

}
