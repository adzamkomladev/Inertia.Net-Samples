using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Driver;

namespace CarParker.Features.Lots;

public enum SpotType { Standard, Compact, Handicap, Covered, Ev }
public enum SpotState { Free, Booked, Occupied, Blocked }

/// <summary>A parking lot. Money is cents-long matching the rest of the domain; the hourly rate is per spot hour.</summary>
public sealed class Lot
{
    [BsonId] public Guid Id { get; set; } = Guid.CreateVersion7();
    public required string Name { get; set; }
    public required string Address { get; set; }
    /// <summary>Display-only area name (e.g. "City Center"), used on cards and search snippets.</summary>
    public required string Area { get; set; }
    public double? Lat { get; set; }
    public double? Lng { get; set; }
    public int LevelCount { get; set; } = 2;
    public long RatePerHourCents { get; set; } = 400;
    public long DailyMaxCents { get; set; } = 2400;
    public string? ContactPhone { get; set; }
    /// <summary>Shown to customers before arrival: entrance, height limits, gate codes.</summary>
    [BsonIgnoreIfNull] public string? ArrivalNotes { get; set; }
    public bool Open24Hours { get; set; } = true;
    public string Opens { get; set; } = "06:00";
    public string Closes { get; set; } = "22:00";
    public string TimeZoneId { get; set; } = "UTC";
    public bool IsActive { get; set; } = true;
    /// <summary>Every lot auto-enrolls in Spot Cover; the flat coverage is configurable per lot.</summary>
    public bool InsuranceCovered { get; set; } = true;
    public long CoverageCents { get; set; } = 50_000;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    [BsonIgnoreIfNull] public string? DemoKey { get; set; }
}

public sealed class Spot
{
    [BsonId] public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid LotId { get; set; }
    public required string Label { get; set; } // "A-01" .. "D-24" (section letters × numbers)
    [BsonRepresentation(BsonType.String)] public SpotType Type { get; set; } = SpotType.Standard;
    public int Level { get; set; } = 1;
    [BsonRepresentation(BsonType.String)] public SpotState State { get; set; } = SpotState.Free;
    /// <summary>Guard/admin note recorded with the last manual block/clear (manual override audit).</summary>
    [BsonIgnoreIfNull] public string? CheckpointNote { get; set; }
    [BsonIgnoreIfNull] public DateTime? LastCheckpointAt { get; set; }
    public bool Active { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
