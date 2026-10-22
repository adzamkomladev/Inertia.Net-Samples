using MongoDB.Driver;
using CarParker.Data;
using CarParker.Features.Alerts;
using CarParker.Features.Bookings;
using CarParker.Features.Lots;
using CarParker.Infrastructure;

namespace CarParker.Features.Guard;

public static class SessionQueries
{
    public static Task<bool> HasOpenSessionAsync(this AppDb db, Guid spotId, CancellationToken ct) =>
        db.ParkingSessions.Find(s => s.SpotId == spotId && s.Status == SessionStatus.InProgress).AnyAsync(ct);
}
