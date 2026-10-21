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

/// Reviews: verified-purchase gate, one per user per product (a second post edits it), and rating aggregates that follow published rows.
public sealed class ReviewsService(AppDb db)
{
    const int PageSize = 25;

    public async Task<SaveResult> WriteAsync(Guid uid, ReviewInput input, CancellationToken ct)
    {
        if (input.Rating is < 1 or > 5) return SaveResult.Invalid("rating", "Pick 1 to 5 stars.");
        if (string.IsNullOrWhiteSpace(input.Body)) return SaveResult.Invalid("body", "Write a short review.");
        if (!await db.Products.AnyAsync(p => p.Id == input.ProductId, ct)) return SaveResult.Missing;

        var purchase = await db.OrderItems
            .Where(i => i.ProductId == input.ProductId && i.Order.UserId == uid && i.Order.Status == OrderStatuses.Delivered)
            .OrderByDescending(i => i.Order.DeliveredAt)
            .Select(i => (Guid?)i.Id)
            .FirstOrDefaultAsync(ct);
        if (purchase is null) return SaveResult.Error("Reviews open once your delivery of this item completes.");

        var review = await db.Reviews.FirstOrDefaultAsync(r => r.ProductId == input.ProductId && r.UserId == uid, ct);
        var isNew = review is null;
        if (review is null)
            db.Reviews.Add(review = new Review { ProductId = input.ProductId, UserId = uid, OrderItemId = input.OrderItemId ?? purchase, Body = input.Body.Trim() });
        review.Rating = input.Rating;
        review.Title = input.Title?.Trim();
        review.Body = input.Body.Trim();
        review.OrderItemId ??= purchase;
        review.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        await RecountAsync(input.ProductId, ct);
        return SaveResult.Success(isNew ? "Review published." : "Review updated.");
    }

    /// Product rating aggregates follow the published reviews, computed in the database.
    async Task RecountAsync(Guid productId, CancellationToken ct) =>
        await db.Database.ExecuteSqlAsync($"""
            UPDATE "Products" SET
                "RatingAvg" = COALESCE((SELECT ROUND(AVG("Rating")::numeric, 2) FROM "Reviews" WHERE "ProductId" = {productId} AND "Status" = {ReviewStatuses.Published}), 0),
                "RatingCount" = (SELECT COUNT(*) FROM "Reviews" WHERE "ProductId" = {productId} AND "Status" = {ReviewStatuses.Published})
            WHERE "Id" = {productId}
            """, ct);

    // ---------------------------------------------------------------- admin moderation

    public Task<Paged<AdminReviewRow>> AdminReviewsAsync(string? state, int? page, CancellationToken ct)
    {
        var query = db.Reviews.AsNoTracking().IgnoreQueryFilters();
        if (state == ReviewStatuses.Hidden) query = query.Where(r => r.Status == ReviewStatuses.Hidden);
        return query
            .OrderByDescending(r => r.CreatedAt).ThenByDescending(r => r.Id)
            .Select(r => new AdminReviewRow(r.Id, r.Product.Name, r.Product.Slug, r.User.Name, r.Rating, r.Title, r.Body, r.Status, r.CreatedAt))
            .ToPagedAsync(page, PageSize, ct);
    }

    public async Task<SaveResult> SetStatusAsync(Guid id, ReviewStateInput input, CancellationToken ct)
    {
        var review = await db.Reviews.IgnoreQueryFilters().AsNoTracking().Where(r => r.Id == id)
            .Select(r => new { r.ProductId, r.Status }).FirstOrDefaultAsync(ct);
        if (review is null) return SaveResult.Missing;
        var status = input.State is ReviewStatuses.Published or "unhide" ? ReviewStatuses.Published : ReviewStatuses.Hidden;
        if (status == review.Status) return SaveResult.Done;
        await db.Reviews.Where(r => r.Id == id).ExecuteUpdateAsync(s => s.SetProperty(r => r.Status, status), ct);
        await RecountAsync(review.ProductId, ct);
        return SaveResult.Success(status == ReviewStatuses.Hidden ? "Review hidden." : "Review restored.");
    }
}
