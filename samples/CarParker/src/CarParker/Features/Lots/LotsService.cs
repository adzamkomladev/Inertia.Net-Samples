using Inertia.Net;
using MongoDB.Bson;
using MongoDB.Driver;
using CarParker.Data;
using CarParker.Infrastructure;
using CarParker.Features.Bookings;

namespace CarParker.Features.Lots;

public sealed record LotCard(
    Guid Id,
    string Name,
    string Address,
    string Area,
    int LevelCount,
    long RatePerHourCents,
    long DailyMaxCents,
    string? ContactPhone,
    int Spots,
    int Free,
    double OccupancyPct,
    string[] SpotTypes,
    bool InsuranceCovered,
    long CoverageCents)
{
    public static LotCard From(Lot lot, int spots, int free, string[] types) => new(
        lot.Id, lot.Name, lot.Address, lot.Area, lot.LevelCount, lot.RatePerHourCents, lot.DailyMaxCents,
        lot.ContactPhone, spots, free, spots == 0 ? 0 : Math.Round((spots - free) * 100.0 / spots, 1), types,
        lot.InsuranceCovered, lot.CoverageCents);
}

public sealed record SpotView(Guid Id, string Label, string Type, int Level, string State, DateTime? LastCheckpointAt, string? Note);

public sealed record Tariff(long RatePerHourCents, long DailyMaxCents, int InsurancePercent, long InsuranceMinCents, long CoverageCents);

public sealed record DayAvailability(string Date, int Free, int Total);

public sealed record LotOption(Guid Id, string Name);

public sealed record LotsPage(LotCard[] Data, int Page, bool HasMore) : IProvidesScrollMetadata
{
    public ScrollMetadata GetScrollMetadata() => ScrollMetadata.FromPage(Page, HasMore);
}

public static partial class LotsService
{
    internal const int PageSize = 12;

    internal static async Task<LotCard[]> CardsAsync(AppDb db, IReadOnlyCollection<Lot> lots, CancellationToken ct)
    {
        if (lots.Count == 0) return [];
        var ids = lots.Select(lot => lot.Id).ToArray();
        var spotRows = await db.Spots.Find(s => ids.Contains(s.LotId) && s.Active)
            .Project(s => new { s.LotId, s.Type, s.State }).ToListAsync(ct);
        return lots.Select(lot =>
        {
            var own = spotRows.Where(s => s.LotId == lot.Id).ToList();
            return LotCard.From(lot, own.Count, own.Count(s => s.State == SpotState.Free),
                own.Select(s => s.Type.ToString().ToLowerInvariant()).Distinct().OrderBy(x => x).ToArray());
        }).ToArray();
    }

    /// <summary>Active lots ordered for listings and pickers.</summary>
    internal static async Task<List<Lot>> ActiveAsync(AppDb db, CancellationToken ct) =>
        await db.Lots.Find(Builders<Lot>.Filter.Eq(l => l.IsActive, true)).SortBy(l => l.Name).ToListAsync(ct);

    internal static async Task<Lot?> FindAsync(AppDb db, Guid id, CancellationToken ct) =>
        await db.Lots.Find(l => l.Id == id).FirstOrDefaultAsync(ct);

    internal static async Task<Lot?> FindActiveAsync(AppDb db, Guid id, CancellationToken ct) =>
        await db.Lots.Find(l => l.Id == id && l.IsActive).FirstOrDefaultAsync(ct);

    internal static List<LotOption> OptionsFor(IEnumerable<Lot> lots) => lots.Select(l => new LotOption(l.Id, l.Name)).ToList();

    /// <summary>Lots listing (GET /lots): q via Meilisearch when synced (native Mongo regex fallback), type/maxPrice filters,
    /// sort in memory, then one page slice. The engine flag tells the client which search answered.</summary>
    internal static async Task<(LotsPage Page, string Engine)> QueryPageAsync(
        AppDb db, MeilisearchTaskClient? search, OptionalServiceNamespace names, IConfiguration config,
        string? q, string? type, long? maxPrice, string? sort, int page, CancellationToken ct)
    {
        var external = false;
        List<Lot> rows;
        if (string.IsNullOrWhiteSpace(q))
        {
            rows = await db.Lots.Find(Builders<Lot>.Filter.Eq(l => l.IsActive, true)).SortBy(l => l.Name).ToListAsync(ct);
        }
        else
        {
            rows = null!;
            var searchReady = search is not null && SearchIndexing.IsMeilisearchEnabled(config)
                && !await SearchIndexing.IsReindexInProgressAsync(db, ct)
                && !await db.SearchIndexStates.Find(x => x.RequestedVersion > x.AppliedVersion).AnyAsync(ct);
            if (searchReady)
            {
                try
                {
                    var hits = await search!.SearchAsync(names.MeilisearchIndex("lots"), q.Trim(), 100, 0, ct);
                    var ids = hits.DocumentIds.Select(id => Guid.TryParse(id, out var g) ? g : Guid.Empty).Where(id => id != Guid.Empty).ToArray();
                    var byId = (await db.Lots.Find(x => ids.Contains(x.Id) && x.IsActive).ToListAsync(ct)).ToDictionary(l => l.Id);
                    rows = ids.Where(byId.ContainsKey).Select(id => byId[id]).ToList();
                    external = true;
                }
                catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested) { }
            }
            rows ??= await NativeSearchAsync(db, q, ct);
        }

        if (type is { Length: > 0 } && Enum.TryParse<SpotType>(type, ignoreCase: true, out var wanted))
        {
            var withType = await db.Spots.Find(s => s.Active && s.Type == wanted).Project(s => s.LotId).ToListAsync(ct);
            var idSet = withType.ToHashSet();
            rows = rows.Where(l => idSet.Contains(l.Id)).ToList();
        }
        if (maxPrice is { } cap)
            rows = rows.Where(l => l.RatePerHourCents <= cap).ToList();

        var cards = await CardsAsync(db, rows, ct);
        cards = sort switch
        {
            "price" => cards.OrderBy(c => c.RatePerHourCents).ThenBy(c => c.Name, StringComparer.OrdinalIgnoreCase).ToArray(),
            "price_desc" => cards.OrderByDescending(c => c.RatePerHourCents).ThenBy(c => c.Name, StringComparer.OrdinalIgnoreCase).ToArray(),
            "occupancy" => cards.OrderByDescending(c => c.OccupancyPct).ThenBy(c => c.Name, StringComparer.OrdinalIgnoreCase).ToArray(),
            _ => cards.OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase).ToArray(),
        };
        var take = cards.Skip((page - 1) * PageSize).Take(PageSize).ToArray();
        return (new LotsPage(take, page, cards.Length > page * PageSize), external ? "meilisearch" : "native");
    }

    /// <summary>Case-insensitive regex match over name/address/area for the dual-engine fallback.</summary>
    internal static async Task<List<Lot>> NativeSearchAsync(AppDb db, string q, CancellationToken ct)
    {
        var pattern = System.Text.RegularExpressions.Regex.Escape(q.Trim());
        var text = Builders<Lot>.Filter.Or(
            Builders<Lot>.Filter.Regex(l => l.Name, new BsonRegularExpression(pattern, "i")),
            Builders<Lot>.Filter.Regex(l => l.Address, new BsonRegularExpression(pattern, "i")),
            Builders<Lot>.Filter.Regex(l => l.Area, new BsonRegularExpression(pattern, "i")));
        return await db.Lots.Find(text & Builders<Lot>.Filter.Eq(l => l.IsActive, true)).SortBy(l => l.Name).ToListAsync(ct);
    }

    /// <summary>Live spot board (lot page + guard console), ordered level-first for a grid readback.</summary>
    internal static async Task<SpotView[]> SpotsAsync(AppDb db, Guid lotId, CancellationToken ct) =>
        (await db.Spots.Find(s => s.LotId == lotId && s.Active).SortBy(s => s.Level).ThenBy(s => s.Label).ToListAsync(ct))
            .Select(s => new SpotView(s.Id, s.Label, s.Type.ToString().ToLowerInvariant(), s.Level,
                s.State.ToString().ToLowerInvariant(), s.LastCheckpointAt, s.CheckpointNote)).ToArray();

    /// <summary>Next 14 days of free/total spot counts for the availability heat strip on the lot page.</summary>
    internal static async Task<DayAvailability[]> AvailabilityAsync(AppDb db, Lot lot, CancellationToken ct)
    {
        var spots = await db.Spots.Find(s => s.LotId == lot.Id && s.Active).Project(s => s.State).ToListAsync(ct);
        var total = spots.Count;
        var days = new List<DayAvailability>(14);
        var now = DateTime.UtcNow;
        var busy = total - spots.Count(s => s == SpotState.Free);
        for (var i = 0; i < 14; i++)
        {
            var day = DateOnly.FromDateTime(now).AddDays(i);
            var weekday = ((int)day.DayOfWeek + 6) % 7; // Monday = 0 keeps the strip aligned with the wizard chips
            var pressure = weekday is 5 or 6 ? 2 : 0;   // weekends run hotter on average in the demo
            var free = Math.Max(0, total - busy - pressure);
            days.Add(new DayAvailability(day.ToString("yyyy-MM-dd"), free, total));
        }
        return days.ToArray();
    }

    internal static Tariff TariffOf(Lot lot) => new(
        lot.RatePerHourCents, lot.DailyMaxCents, PricingRules.InsurancePercent, PricingRules.InsurancePremiumMinCents, lot.CoverageCents);
}
