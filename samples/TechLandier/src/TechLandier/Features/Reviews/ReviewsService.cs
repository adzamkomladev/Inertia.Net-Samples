using Microsoft.EntityFrameworkCore;
using TechLandier.Data;

namespace TechLandier.Features;

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
    Task RecountAsync(Guid productId, CancellationToken ct) =>
        db.Products.IgnoreQueryFilters().Where(p => p.Id == productId).ExecuteUpdateAsync(s => s
            .SetProperty(p => p.RatingAvg, p => Math.Round(db.Reviews.IgnoreQueryFilters()
                .Where(r => r.ProductId == p.Id && r.Status == ReviewStatuses.Published)
                .Average(r => (decimal?)r.Rating) ?? 0, 2))
            .SetProperty(p => p.RatingCount, p => db.Reviews.IgnoreQueryFilters()
                .Count(r => r.ProductId == p.Id && r.Status == ReviewStatuses.Published)), ct);

    // ---------------------------------------------------------------- admin moderation

    public Task<Paged<AdminReviewRow>> AdminReviewsAsync(string? state, int? page, CancellationToken ct)
    {
        var query = db.Reviews.AsNoTracking().IgnoreQueryFilters();
        var status = state == ReviewStatuses.Hidden ? ReviewStatuses.Hidden : ReviewStatuses.Published;
        query = query.Where(r => r.Status == status);
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
