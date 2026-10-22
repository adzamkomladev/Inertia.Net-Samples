using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Driver;
using CarParker.Data;
using CarParker.Features.Bookings;
using CarParker.Features.Lots;

namespace CarParker.Features.Analytics;

/// <summary>Daily per-lot rollup feeding the admin analytics charts.</summary>
public sealed class LotDailyStat
{
    public Guid LotId { get; set; }
    [BsonRepresentation(BsonType.String)] public DateOnly Date { get; set; }
    [BsonId] public string Id { get => $"{LotId}:{Date:yyyy-MM-dd}"; set { var parts = value.Split(':', 2); LotId = Guid.Parse(parts[0]); Date = DateOnly.Parse(parts[1]); } }
    public double OccupancyPct { get; set; }
    public long RevenueCents { get; set; }
    public int Sessions { get; set; }
    public int Reservations { get; set; }
}

public sealed record KpiSet(
    long RevenueCents, int Sessions, double OccupancyAvgPct, double NoShowPct, int Reservations,
    int RefundsPending, long RefundsPaidCents, double PolicyMixPct);

public sealed record SeriesPoint(string Date, long RevenueCents, int Sessions, double OccupancyPct, int Reservations);

public sealed record RefundMixRow(string Status, int Count);

public sealed record TopLotRow(Guid Id, string Name, long RevenueCents, int Sessions, double OccupancyPct);
