using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Driver;
using CarParker.Data;
using CarParker.Infrastructure;

namespace CarParker.Features.Alerts;

public sealed class Alert
{
    [BsonId] public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid UserId { get; set; }
    /// <summary>reservation_soon | overstay | refund | session | gate | ...</summary>
    public required string Kind { get; set; }
    public required string Title { get; set; }
    [BsonIgnoreIfNull] public string? Body { get; set; }
    [BsonIgnoreIfNull] public string? Href { get; set; }
    public DateTime At { get; set; } = DateTime.UtcNow;
    [BsonIgnoreIfNull] public DateTime? ReadAt { get; set; }
}
