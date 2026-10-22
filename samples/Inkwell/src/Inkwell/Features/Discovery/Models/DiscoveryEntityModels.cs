using Microsoft.EntityFrameworkCore;

namespace Inkwell.Data;

public sealed class SeenPost
{
    public Guid UserId { get; set; }
    public Guid PostId { get; set; }
    public DateTime FirstSeenAt { get; set; }
    public DateTime LastSeenAt { get; set; }
}

public sealed class InterestProfile
{
    public Guid UserId { get; set; }
    public string TopicsJson { get; set; } = "{}";
    public string AuthorsJson { get; set; } = "{}";
    public DateTime UpdatedAt { get; set; }
    public bool Dirty { get; set; } = true;
}

public sealed class FeedSnapshot
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public Guid UserId { get; set; }
    public required string Mode { get; set; }
    public string ItemsJson { get; set; } = "[]";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime ExpiresAt { get; set; } = DateTime.UtcNow.AddMinutes(30);
}

public sealed class DigestDelivery
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid UserId { get; set; }
    public DateOnly LocalDate { get; set; }
    public required string TimeZoneId { get; set; }
    public DateTime WindowStartUtc { get; set; }
    public DateTime WindowEndUtc { get; set; }
    public DateTime ExpiresAtUtc { get; set; }
    public string PostIdsJson { get; set; } = "[]";
    public string Status { get; set; } = "Queued";
    public string? ProviderReference { get; set; }
    public string? LastError { get; set; }
    public int Attempts { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
