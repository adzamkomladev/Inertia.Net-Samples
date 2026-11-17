using MongoDB.Driver;
using CarParker.Data;
using CarParker.Features.Alerts;
using CarParker.Features.Bookings;
using CarParker.Features.Lots;
using CarParker.Infrastructure;

namespace CarParker.Features.Guard;

public static class SessionQueries
{
    public static async Task<bool> HasOpenSessionAsync(this AppDb db, Guid spotId, CancellationToken ct) =>
        await db.ParkingSessions.Find(s => s.SpotId == spotId && s.Status == SessionStatus.InProgress).AnyAsync(ct);

    /// <summary>True when a session is open on one of this reservation's own windows (other bookings of the bay do not count).</summary>
    public static async Task<bool> HasOpenSessionForReservationAsync(this AppDb db, Guid reservationId, CancellationToken ct)
    {
        var occurrenceIds = await db.Occurrences.Find(o => o.ReservationId == reservationId).Project(o => o.Id).ToListAsync(ct);
        return occurrenceIds.Count > 0
            && await db.ParkingSessions.Find(s => s.Status == SessionStatus.InProgress && occurrenceIds.Contains(s.OccurrenceId)).AnyAsync(ct);
    }

    /// <summary>The given bays that a confirmed reservation still holds: a scheduled or active window that has not ended.
    /// Two reads whatever the number of bays.</summary>
    public static async Task<HashSet<Guid>> HeldSpotIdsAsync(this AppDb db, IReadOnlyCollection<Guid> spotIds, DateTime now, CancellationToken ct)
    {
        if (spotIds.Count == 0) return [];
        var ids = spotIds.ToArray();
        var holders = await db.Reservations.Find(r => ids.Contains(r.SpotId) && r.Status == ReservationStatus.Confirmed)
            .Project(r => new { r.Id, r.SpotId }).ToListAsync(ct);
        if (holders.Count == 0) return [];
        var spotOf = holders.ToDictionary(h => h.Id, h => h.SpotId);
        var holderIds = spotOf.Keys.ToArray();
        var live = await db.Occurrences.Find(o => holderIds.Contains(o.ReservationId)
                && (o.Status == OccurrenceStatus.Scheduled || o.Status == OccurrenceStatus.Active) && o.EndAtUtc > now)
            .Project(o => o.ReservationId).ToListAsync(ct);
        return live.Select(id => spotOf[id]).ToHashSet();
    }

    public static async Task<SpotState?> SpotStateAsync(this AppDb db, Guid spotId, CancellationToken ct) =>
        await db.Spots.Find(s => s.Id == spotId).Project(s => (SpotState?)s.State).FirstOrDefaultAsync(ct);
}
