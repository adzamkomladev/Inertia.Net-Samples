using Inertia.Net;
using MongoDB.Driver;
using CarParker.Data;
using CarParker.Features.Bookings;

namespace CarParker.Features.Lots;

public sealed class LotsService(AppDb db)
{
    public const int PageSize = 12;

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
            return Card(lot, own.Sum(g => g.Count), own.Where(g => g.State == SpotState.Free).Sum(g => g.Count),
                own.Select(g => g.Type.ToString().ToLowerInvariant()).Distinct().Order().ToArray());
        }).ToArray();
    }

    /// <summary>The card for a lot whose active spots are already loaded, so detail pages do not read them twice.</summary>
    public static LotCard CardFromSpots(Lot lot, IReadOnlyCollection<SpotView> spots)
    {
        var free = spots.Count(s => s.State == "free");
        return Card(lot, spots.Count, free, spots.Select(s => s.Type).Distinct().Order().ToArray());
    }

    static LotCard Card(Lot lot, int spots, int free, string[] spotTypes) =>
        new(lot.Id, lot.Name, lot.Address, lot.Area, lot.LevelCount, lot.RatePerHourCents, lot.DailyMaxCents,
            lot.ContactPhone, spots, free, spots == 0 ? 0 : Math.Round((spots - free) * 100.0 / spots, 1),
            spotTypes, lot.InsuranceCovered, lot.CoverageCents, lot.IsActive, lot.Open24Hours, lot.Opens, lot.Closes, lot.TimeZoneId, lot.ArrivalNotes);

    public async Task<List<Lot>> ActiveAsync(CancellationToken ct) =>
        await db.Lots.Find(l => l.IsActive).SortBy(l => l.Name).ToListAsync(ct);

    public async Task<Lot?> FindActiveAsync(Guid id, CancellationToken ct) =>
        await db.Lots.Find(l => l.Id == id && l.IsActive).FirstOrDefaultAsync(ct);

    public async Task<Page<LotCard>> QueryPageAsync(string? q, string? type, long? maxPrice, string? sort, PageRequest request, CancellationToken ct)
    {
        var filter = Builders<Lot>.Filter.Eq(l => l.IsActive, true);
        if (!string.IsNullOrWhiteSpace(q)) filter &= Builders<Lot>.Filter.Text(q.Trim());
        if (maxPrice is { } cap) filter &= Builders<Lot>.Filter.Lte(l => l.RatePerHourCents, cap);
        if (Enum.TryParse<SpotType>(type, ignoreCase: true, out var wanted))
            filter &= Builders<Lot>.Filter.In(l => l.Id, await db.Spots.Distinct(s => s.LotId, s => s.Active && s.Type == wanted).ToListAsync(ct));

        if (sort == "occupancy")
        {
            // Occupation ranking has no stable Mongo sort without an aggregation, so it pages the filtered cards in memory.
            var lots = await db.Lots.Find(filter).ToListAsync(ct);
            var byName = StringComparer.OrdinalIgnoreCase;
            var all = (await CardsAsync(lots, ct)).OrderByDescending(c => c.OccupancyPct).ThenBy(c => c.Name, byName).ToArray();
            var data = all.Skip((request.Page - 1) * request.PerPage).Take(request.PerPage).ToArray();
            return new Page<LotCard>(data, request.Page, request.PerPage, all.Length, all.Length > request.Page * request.PerPage);
        }

        var order = sort switch
        {
            "price" => Builders<Lot>.Sort.Ascending(l => l.RatePerHourCents).Ascending(l => l.Name),
            "price_desc" => Builders<Lot>.Sort.Descending(l => l.RatePerHourCents).Ascending(l => l.Name),
            _ => Builders<Lot>.Sort.Ascending(l => l.Name),
        };
        // Inertia.Net.MongoDB replaces Skip/Limit and counts the filter, so the page carries total/lastPage as well.
        var page = await db.Lots.Find(filter).Sort(order.Ascending(l => l.Id))
            .ToPageAsync(request, ct: ct);
        var cards = await CardsAsync(page.Data, ct);
        return new Page<LotCard>(cards, page.CurrentPage, page.PerPage, page.Total, page.HasMore, page.PageName);
    }

    /// <summary>Live spot board (lot page and guard console), level-first.</summary>
    public async Task<SpotView[]> SpotsAsync(Guid lotId, CancellationToken ct) =>
        (await db.Spots.Find(s => s.LotId == lotId && s.Active).SortBy(s => s.Level).ThenBy(s => s.Label).ToListAsync(ct))
            .Select(s => new SpotView(s.Id, s.Label, s.Type.ToString().ToLowerInvariant(), s.Level,
                s.State.ToString().ToLowerInvariant(), s.LastCheckpointAt, s.CheckpointNote)).ToArray();

}
