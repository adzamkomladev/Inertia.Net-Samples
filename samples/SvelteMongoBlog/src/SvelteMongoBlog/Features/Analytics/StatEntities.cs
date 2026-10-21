using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace SvelteMongoBlog.Features.Analytics;

/// <summary>Daily per-lot rollup feeding the admin analytics charts (same shape of idea as the old post_daily_stats).</summary>
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
