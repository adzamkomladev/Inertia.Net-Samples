using VuePgBlog.Data;
namespace VuePgBlog.Features;

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
    /// Verified-purchase gate: the delivered order item that unlocked writing.
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
