using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Driver;
using CarParker.Data;
using CarParker.Features.Bookings;
using CarParker.Features.Lots;
using Inertia.Net;

namespace CarParker.Features.Analytics;

/// <summary>Admin analytics over lot_daily_stats, payments and refunds. A lot id of Guid.Empty means all lots. Days are
/// calendar days on each lot's clock; an all-lots KPI window runs on the operator's clock (LotTime.DefaultZoneId).</summary>
public sealed class LotAnalyticsService(AppDb db, LotsService lots, IInertiaCache cache)
{
    /// <summary>The zone a view counts days in: the lot's own, or the operator's for all lots.</summary>
    async Task<TimeZoneInfo> ViewZoneAsync(Guid lotId, CancellationToken ct) => lotId == Guid.Empty
        ? LotTime.DefaultZone
        : LotTime.Zone(await db.Lots.Find(l => l.Id == lotId).Project(l => l.TimeZoneId).FirstOrDefaultAsync(ct) ?? LotTime.DefaultZoneId);

    /// <summary>KPIs over the last <paramref name="days"/> days. The reads are independent, so they run together.</summary>
    public async Task<KpiSet> KpisAsync(Guid lotId, int days, CancellationToken ct)
    {
        var zone = await ViewZoneAsync(lotId, ct);
        var today = LotTime.Today(DateTime.UtcNow, zone);
        var from = LotTime.StartOfDay(today.AddDays(1 - days), zone);
        var until = LotTime.StartOfDay(today.AddDays(1), zone);
        var scoped = lotId != Guid.Empty;
        var reservations = await db.Reservations.Find(r => (!scoped || r.LotId == lotId) && r.CreatedAt >= from && r.CreatedAt < until)
            .Project(r => new { r.Id, r.InsuranceId }).ToListAsync(ct);
        var ids = reservations.Select(r => r.Id).ToArray();
        var firstDay = today.AddDays(1 - days);
        var lastDay = today;

        var payments = db.Payments.Find(p => ids.Contains(p.ReservationId) && p.Status == PaymentStatus.Paid && p.PaidAt >= from && p.PaidAt < until)
            .Project(p => p.AmountCents).ToListAsync(ct);
        var sessions = db.ParkingSessions.CountDocumentsAsync(s => (!scoped || s.LotId == lotId) && s.EnteredAtUtc >= from && s.EnteredAtUtc < until, cancellationToken: ct);
        var settled = db.Occurrences.Find(o => ids.Contains(o.ReservationId) && (o.Status == OccurrenceStatus.Done || o.Status == OccurrenceStatus.NoShow))
            .Project(o => o.Status).ToListAsync(ct);
        var refunds = db.Refunds.Find(rf => ids.Contains(rf.ReservationId)).Project(rf => new { rf.Status, rf.AmountCents }).ToListAsync(ct);
        var occupancy = db.LotDailyStats.Find(s => s.Date >= firstDay && s.Date <= lastDay && (!scoped || s.LotId == lotId))
            .Project(s => s.OccupancyPct).ToListAsync(ct);
        await Task.WhenAll(payments, sessions, settled, refunds, occupancy);

        var noShows = settled.Result.Count(s => s == OccurrenceStatus.NoShow);
        return new KpiSet(
            RevenueCents: payments.Result.Sum(),
            Sessions: (int)sessions.Result,
            OccupancyAvgPct: occupancy.Result.Count == 0 ? 0 : Math.Round(occupancy.Result.Average(), 1),
            NoShowPct: settled.Result.Count == 0 ? 0 : Math.Round(noShows * 100.0 / settled.Result.Count, 1),
            Reservations: reservations.Count,
            RefundsPending: refunds.Result.Count(rf => rf.Status == RefundStatus.Requested),
            RefundsPaidCents: refunds.Result.Where(rf => rf.Status == RefundStatus.Paid).Sum(rf => rf.AmountCents),
            PolicyMixPct: reservations.Count == 0 ? 0 : Math.Round(reservations.Count(r => r.InsuranceId != null) * 100.0 / reservations.Count, 1));
    }

    /// <summary>Daily revenue, sessions, occupancy and reservations with zero-filled days.</summary>
    public async Task<SeriesPoint[]> SeriesAsync(Guid lotId, int days, CancellationToken ct)
    {
        var since = LotTime.Today(DateTime.UtcNow, await ViewZoneAsync(lotId, ct)).AddDays(1 - days);
        var scoped = lotId != Guid.Empty;
        var byDate = (await db.LotDailyStats.Find(s => s.Date >= since && (!scoped || s.LotId == lotId)).ToListAsync(ct))
            .GroupBy(s => s.Date).ToDictionary(g => g.Key, g => new SeriesPoint(
                g.Key.ToString("yyyy-MM-dd"), g.Sum(x => x.RevenueCents), g.Sum(x => x.Sessions), g.Average(x => x.OccupancyPct), g.Sum(x => x.Reservations)));
        return Enumerable.Range(0, days).Select(i => since.AddDays(i))
            .Select(day => byDate.GetValueOrDefault(day) ?? new SeriesPoint(day.ToString("yyyy-MM-dd"), 0, 0, 0, 0)).ToArray();
    }

    /// <summary>Best lots of the last seven days.</summary>
    public async Task<TopLotRow[]> TopLotsAsync(CancellationToken ct)
    {
        var since = LotTime.Today(DateTime.UtcNow, LotTime.DefaultZone).AddDays(-7);
        var rows = db.LotDailyStats.Find(s => s.Date >= since).ToListAsync(ct);
        var names = lots.ActiveAsync(ct);
        await Task.WhenAll(rows, names);
        var namesById = names.Result.ToDictionary(l => l.Id, l => l.Name);
        return rows.Result.GroupBy(r => r.LotId)
            .Select(g => new TopLotRow(g.Key, namesById.GetValueOrDefault(g.Key, "Lot"), g.Sum(x => x.RevenueCents), g.Sum(x => x.Sessions), Math.Round(g.Average(x => x.OccupancyPct), 1)))
            .OrderByDescending(x => x.RevenueCents).Take(5).ToArray();
    }

    public async Task<RefundMixRow[]> RefundMixAsync(CancellationToken ct)
    {
        var since = LotTime.StartOfDay(LotTime.Today(DateTime.UtcNow, LotTime.DefaultZone).AddDays(-14), LotTime.DefaultZone);
        var groups = await db.Refunds.Aggregate().Match(rf => rf.CreatedAt >= since)
            .Group(rf => rf.Status, g => new { Status = g.Key, Count = g.Count() }).ToListAsync(ct);
        return groups.Select(g => new RefundMixRow(g.Status.ToString().ToLowerInvariant(), g.Count)).ToArray();
    }

    /// <summary>Cron rollup: rebuilds the last <paramref name="days"/> days (yesterday and today by default) for every active
    /// lot, each day running midnight to midnight on that lot's clock (occupancy from session overlap, revenue from paid
    /// payments). One read per collection covers every lot's window; the split by lot and day happens in memory.</summary>
    public async Task RollupAsync(CancellationToken ct, int days = 2)
    {
        var active = await lots.ActiveAsync(ct);
        if (active.Count == 0) return;
        var lotIds = active.Select(l => l.Id).ToArray();
        var spotCounts = (await db.Spots.Aggregate().Match(s => lotIds.Contains(s.LotId) && s.Active)
            .Group(s => s.LotId, g => new { LotId = g.Key, Count = g.Count() }).ToListAsync(ct)).ToDictionary(g => g.LotId, g => g.Count);

        var now = DateTime.UtcNow;
        var windows = active.Select(lot =>
        {
            var zone = LotTime.Zone(lot.TimeZoneId);
            var today = LotTime.Today(now, zone);
            return (Lot: lot, Days: Enumerable.Range(0, days).Select(i => today.AddDays(-i))
                .Select(day => (Day: day, Start: LotTime.StartOfDay(day, zone), End: LotTime.StartOfDay(day.AddDays(1), zone))).ToArray());
        }).ToArray();
        var from = windows.SelectMany(w => w.Days).Min(d => d.Start);
        var until = windows.SelectMany(w => w.Days).Max(d => d.End);

        var sessions = db.ParkingSessions.Find(s => lotIds.Contains(s.LotId) && s.EnteredAtUtc < until && (s.ExitedAtUtc == null || s.ExitedAtUtc > from))
            .Project(s => new { s.LotId, s.EnteredAtUtc, s.ExitedAtUtc }).ToListAsync(ct);
        var created = db.Reservations.Find(r => lotIds.Contains(r.LotId) && r.CreatedAt >= from && r.CreatedAt < until)
            .Project(r => new { r.LotId, r.CreatedAt }).ToListAsync(ct);
        var payments = db.Payments.Find(p => p.Status == PaymentStatus.Paid && p.PaidAt >= from && p.PaidAt < until)
            .Project(p => new { p.ReservationId, p.AmountCents, p.PaidAt }).ToListAsync(ct);
        await Task.WhenAll(sessions, created, payments);

        var paidReservationIds = payments.Result.Select(p => p.ReservationId).Distinct().ToArray();
        var lotByReservation = (await db.Reservations.Find(r => paidReservationIds.Contains(r.Id)).Project(r => new { r.Id, r.LotId }).ToListAsync(ct))
            .ToDictionary(r => r.Id, r => r.LotId);
        var sessionsByLot = sessions.Result.ToLookup(s => s.LotId);
        var createdByLot = created.Result.ToLookup(r => r.LotId);
        var paidByLot = payments.Result.Where(p => lotByReservation.ContainsKey(p.ReservationId)).ToLookup(p => lotByReservation[p.ReservationId]);

        var writes = new List<WriteModel<LotDailyStat>>();
        foreach (var (lot, lotDays) in windows)
        {
            var spots = spotCounts.GetValueOrDefault(lot.Id, lot.LevelCount * 12); // a lot with no spot rows is sized like a fresh A-D grid
            foreach (var (day, dayStart, dayEnd) in lotDays)
            {
                var own = sessionsByLot[lot.Id].Where(s => s.EnteredAtUtc < dayEnd && (s.ExitedAtUtc ?? DateTime.MaxValue) > dayStart).ToList();
                var minutes = own.Sum(s => Overlap(s.EnteredAtUtc, s.ExitedAtUtc ?? dayEnd, dayStart, dayEnd));
                var stat = new LotDailyStat
                {
                    LotId = lot.Id,
                    Date = day,
                    OccupancyPct = spots == 0 ? 0 : Math.Min(100.0, Math.Round(minutes * 100 / (spots * (dayEnd - dayStart).TotalMinutes), 1)),
                    RevenueCents = paidByLot[lot.Id].Where(p => p.PaidAt >= dayStart && p.PaidAt < dayEnd).Sum(p => p.AmountCents),
                    Sessions = own.Count(s => s.EnteredAtUtc >= dayStart),
                    Reservations = createdByLot[lot.Id].Count(r => r.CreatedAt >= dayStart && r.CreatedAt < dayEnd),
                };
                writes.Add(new ReplaceOneModel<LotDailyStat>(Builders<LotDailyStat>.Filter.Where(s => s.LotId == lot.Id && s.Date == day), stat) { IsUpsert = true });
            }
        }
        await db.LotDailyStats.BulkWriteAsync(writes, cancellationToken: ct);
        await AnalyticsCache.RollupChangedAsync(cache, ct);
    }

    static double Overlap(DateTime entered, DateTime exited, DateTime dayStart, DateTime dayEnd)
    {
        var start = entered < dayStart ? dayStart : entered;
        var end = exited > dayEnd ? dayEnd : exited;
        return end <= start ? 0 : (end - start).TotalMinutes;
    }
}
