using Microsoft.EntityFrameworkCore;
using TechLandier.Data;

namespace TechLandier.Features;

public static class ReviewStatuses
{
    public const string Published = "published";
    public const string Hidden = "hidden";
}

public sealed class Review
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid ProductId { get; set; }
    public Product Product { get; set; } = null!;
    public Guid UserId { get; set; }
    public User User { get; set; } = null!;
    /// The delivered order item that unlocked writing (verified purchase).
    public Guid? OrderItemId { get; set; }
    public OrderItem? OrderItem { get; set; }
    public int Rating { get; set; }
    public string? Title { get; set; }
    public required string Body { get; set; }
    public string Status { get; set; } = ReviewStatuses.Published;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public string? DemoKey { get; set; }
}

public sealed record ReviewInput(Guid ProductId, int Rating, string? Title, string? Body, Guid? OrderItemId);
public sealed record ReviewStateInput(string? State);
public sealed record AdminReviewRow(Guid Id, string Product, string Slug, string Author, int Rating, string? Title, string Body, string Status, DateTime CreatedAt);
