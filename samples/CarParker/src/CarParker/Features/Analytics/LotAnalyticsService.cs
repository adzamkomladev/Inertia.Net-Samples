using MongoDB.Driver;
using CarParker.Data;
using CarParker.Features.Bookings;
using CarParker.Features.Lots;

namespace CarParker.Features.Analytics;

public sealed record KpiSet(
    long RevenueCents,
    int Sessions,
    double OccupancyAvgPct,
    double NoShowPct,
    int Reservations,
    int RefundsPending,
    long RefundsPaidCents,
    double PolicyMixPct);

public sealed record SeriesPoint(string Date, long RevenueCents, int Sessions, double OccupancyPct, int Reservations);

public sealed record RefundMixRow(string Status, int Count);

public sealed record TopLotRow(Guid Id, string Name, long RevenueCents, int Sessions, double OccupancyPct);

/// <summary>Admin analytics data over <c>lot_daily_stats</c>, payments and refunds. The admin handler defers
/// these loads into groups and caches them 60 seconds with the key pattern <c>lots:{lotId}:{days}:{prop}</c>;
/// lotId Guid.Empty means "all lots".</summary>
public static class LotAnalytics
{
    static (DateTime From, DateTime Until) Window(int days)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        return (today.AddDays(1 - days).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc),
            today.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc));
    }

    /// <summary>KPIs over the last `days` days, scoped to a lot or the whole platform.</summary>
    public static async Task<KpiSet> KpisAsync(AppDb db, Guid lotId, int days, CancellationToken ct)
    {
        var (from, until) = Window(days);
        var scoped = lotId != Guid.Empty;
        var reservations = await db.Reservations.Find(r => (!scoped || r.LotId == lotId)
                && r.CreatedAt >= from && r.CreatedAt < until).ToListAsync(ct);
        var reservationIds = reservations.Select(r => r.Id).ToArray();
        var payments = reservationIds.Length == 0
            ? []
            : await db.Payments.Find(p => reservationIds.Contains(p.ReservationId)
                && p.Status == PaymentStatus.Paid && p.PaidAt >= from && p.PaidAt < until).ToListAsync(ct);

        var sessions = scoped
            ? await db.ParkingSessions.CountDocumentsAsync(s => s.LotId == lotId && s.EnteredAtUtc >= from && s.EnteredAtUtc < until, cancellationToken: ct)
            : await db.ParkingSessions.CountDocumentsAsync(s => s.EnteredAtUtc >= from && s.EnteredAtUtc < until, cancellationToken: ct);

        var noShows = 0;
        var settled = 0;
        if (reservationIds.Length > 0)
        {
            var occurrences = await db.Occurrences.Find(o => reservationIds.Contains(o.ReservationId)
                && (o.Status == OccurrenceStatus.Done || o.Status == OccurrenceStatus.NoShow)).ToListAsync(ct);
            noShows = occurrences.Count(o => o.Status == OccurrenceStatus.NoShow);
            settled = occurrences.Count;
        }

        var refundRows = reservationIds.Length == 0
            ? []
            : await db.Refunds.Find(rf => reservationIds.Contains(rf.ReservationId)).ToListAsync(ct);

        var dateStart = DateOnly.FromDateTime(from);
        var dateEnd = DateOnly.FromDateTime(until).AddDays(-1);
        var statRows = await db.LotDailyStats.Find(s => s.Date >= dateStart && s.Date <= dateEnd
            && (!scoped || s.LotId == lotId)).ToListAsync(ct);
        var occupancyAvg = statRows.Count == 0 ? 0 : Math.Round(statRows.Average(s => s.OccupancyPct), 1);

        return new KpiSet(
            RevenueCents: payments.Sum(p => p.AmountCents),
            Sessions: (int)sessions,
            OccupancyAvgPct: occupancyAvg,
            NoShowPct: settled == 0 ? 0 : Math.Round(noShows * 100.0 / settled, 1),
            Reservations: reservations.Count,
            RefundsPending: refundRows.Count(rf => rf.Status == RefundStatus.Requested),
            RefundsPaidCents: refundRows.Where(rf => rf.Status == RefundStatus.Paid).Sum(rf => rf.AmountCents),
            PolicyMixPct: reservations.Count == 0 ? 0 : Math.Round(reservations.Count(r => r.InsuranceId != null) * 100.0 / reservations.Count, 1));
    }

    /// <summary>Daily revenue/sessions/occupancy/reservations series with zero-filled days.</summary>
    public static async Task<SeriesPoint[]> SeriesAsync(AppDb db, Guid lotId, DateOnly since, int days, CancellationToken ct)
    {
        var scoped = lotId != Guid.Empty;
        var rows = await db.LotDailyStats.Find(s => s.Date >= since
            && (!scoped || s.LotId == lotId)).ToListAsync(ct);
        var byDate = rows.GroupBy(s => s.Date).ToDictionary(g => g.Key, g => new SeriesPoint(
            g.Key.ToString("yyyy-MM-dd"),
            g.Sum(x => x.RevenueCents),
            g.Sum(x => x.Sessions),
            scoped ? g.Average(x => x.OccupancyPct) : g.Average(x => x.OccupancyPct),
            g.Sum(x => x.Reservations)));
        var series = new List<SeriesPoint>(days);
        for (var i = 0; i < days; i++)
        {
            var day = since.AddDays(i);
            if (byDate.TryGetValue(day, out var point)) series.Add(point);
            else series.Add(new SeriesPoint(day.ToString("yyyy-MM-dd"), 0, 0, 0, 0));
        }
        return series.ToArray();
    }

    /// <summary>Best-performing lots of the trailing window for the BarList.</summary>
    public static async Task<TopLotRow[]> TopLotsAsync(AppDb db, CancellationToken ct)
    {
        var since = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-7);
        var rows = await db.LotDailyStats.Find(s => s.Date >= since).ToListAsync(ct);
        var lots = await LotsService.ActiveAsync(db, ct);
        var namesById = lots.ToDictionary(l => l.Id, l => l.Name);
        return rows.GroupBy(r => r.LotId)
            .Select(g => new TopLotRow(g.Key,
                namesById.GetValueOrDefault(g.Key, "Lot"),
                g.Sum(x => x.RevenueCents),
                g.Sum(x => x.Sessions),
                Math.Round(g.Average(x => x.OccupancyPct), 1)))
            .OrderByDescending(x => x.RevenueCents).Take(5).ToArray();
    }

    public static async Task<RefundMixRow[]> RefundMixAsync(AppDb db, CancellationToken ct)
    {
        var since = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-14).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var rows = await db.Refunds.Find(rf => rf.CreatedAt >= since).ToListAsync(ct);
        return Enum.GetValues<RefundStatus>()
            .Select(status => new RefundMixRow(status.ToString().ToLowerInvariant(), rows.Count(rf => rf.Status == status)))
            .Where(row => row.Count > 0)
            .ToArray();
    }

    /// <summary>Cron rollup: rebuild yesterday + today per lot (occupancy from session overlap, revenue from paid payments).</summary>
    public static async Task RollupAsync(AppDb db, CancellationToken ct)
    {
        var nowUtc = DateTime.UtcNow;
        var lots = await LotsService.ActiveAsync(db, ct);
        var spotCounts = await db.Spots.Find(_ => true)
            .Project(s => new { s.LotId, s.Active }).ToListAsync(ct);
        var spotsPerLot = spotCounts.GroupBy(s => s.LotId).ToDictionary(g => g.Key, g => g.Count(x => x.Active));

        var sessionRows = await db.ParkingSessions.Find(_ => true).ToListAsync(ct);
        var reservationRows = await db.Reservations.Find(_ => true).ToListAsync(ct);
        var lotByReservation = reservationRows.ToDictionary(r => r.Id, r => r.LotId);
        var paymentRows = await db.Payments.Find(p => p.Status == PaymentStatus.Paid).ToListAsync(ct);

        foreach (var day in new[] { DateOnly.FromDateTime(nowUtc).AddDays(-1), DateOnly.FromDateTime(nowUtc) })
        {
            var dayStart = day.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
            var dayEnd = dayStart.AddDays(1);
            var minutes = 24 * 60.0;

            foreach (var lot in lots.ToArray())
            {
                var shares = sessionRows.Where(s => s.LotId == lot.Id)
                    .Select(s => OverlapMinutes(s.EnteredAtUtc, s.ExitedAtUtc ?? dayEnd, dayStart, dayEnd))
                    .Sum();
                var spotCount = spotsPerLot.GetValueOrDefault(lot.Id, LotMinimumSpots(lot));
                var occupancy = spotCount == 0 ? 0 : Math.Min(100.0, Math.Round(shares * 100 / (spotCount * minutes), 1));
                var sessions = sessionRows.Count(s => s.LotId == lot.Id && s.EnteredAtUtc >= dayStart && s.EnteredAtUtc < dayEnd);
                var revenue = paymentRows
                    .Where(p => p.PaidAt >= dayStart && p.PaidAt < dayEnd && lotByReservation.TryGetValue(p.ReservationId, out var lotId) && lotId == lot.Id)
                    .Sum(p => p.AmountCents);
                var reservations = reservationRows.Count(r => r.LotId == lot.Id && r.CreatedAt >= dayStart && r.CreatedAt < dayEnd);

                var stat = new LotDailyStat
                {
                    LotId = lot.Id,
                    Date = day,
                    OccupancyPct = occupancy,
                    RevenueCents = revenue,
                    Sessions = sessions,
                    Reservations = reservations,
                };
                await db.LotDailyStats.ReplaceOneAsync(
                    s => s.LotId == lot.Id && s.Date == day,
                    stat, new ReplaceOptions { IsUpsert = true }, cancellationToken: ct);
            }

            // Lots without activity still get a zero row so day series stay aligned.
            var activeRows = lots.ToArray();
            if (activeRows.Length > 0)
            {
                var existing = await db.LotDailyStats.Find(s => s.Date == day).ToListAsync(ct);
                var missing = activeRows.Select(l => l.Id).Except(existing.Select(e => e.LotId));
                foreach (var lotId in missing)
                {
                    var spotCount = spotsPerLot.GetValueOrDefault(lotId, 0);
                    var stat = new LotDailyStat
                    {
                        LotId = lotId,
                        Date = day,
                        OccupancyPct = 0,
                        RevenueCents = 0,
                        Sessions = 0,
                        Reservations = 0,
                    };
                    await db.LotDailyStats.ReplaceOneAsync(
                        s => s.LotId == lotId && s.Date == day,
                        stat, new ReplaceOptions { IsUpsert = true }, cancellationToken: ct);
                }
            }
        }
    }

    const int DefaultSpotsPerLot = 24;

    static int LotMinimumSpots(Lot lot) => lot.LevelCount * 12; // fresh A–D grid: 12 spots per level section

    /// <summary>Overlap between one session and the rolled-up UTC day.</summary>
    static double OverlapMinutes(DateTime entered, DateTime exited, DateTime dayStart, DateTime dayEnd)
    {
        var start = entered < dayStart ? dayStart : entered;
        var end = exited > dayEnd ? dayEnd : exited;
        if (end <= start) return 0;
        return (end - start).TotalMinutes;
    }
}
