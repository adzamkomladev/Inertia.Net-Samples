using Microsoft.EntityFrameworkCore;
using VuePgBlog.Data;
using VuePgBlog.Infrastructure;

namespace VuePgBlog.Features;

/// Reviews: verified-purchase gate, one per user per product (upsert edit), aggregates follow published rows.
public static partial class ReviewsService
{
    internal static async Task<IResult> WriteAsync(Guid productId, int rating, string? title, string? body, Guid? orderItemId, HttpContext ctx, AppDb db, CancellationToken ct)
    {
        var uid = ctx.User.UserId();
        if (rating is < 1 or > 5)
            return Back().WithErrors(new Dictionary<string, string> { ["rating"] = "Pick 1 to 5 stars." });
        if (string.IsNullOrWhiteSpace(body))
            return Back().WithErrors(new Dictionary<string, string> { ["body"] = "Write a short review." });

        var product = await db.Products.AsNoTracking()
            .Where(p => p.Id == productId)
            .Select(p => new { p.Id, p.Slug })
            .FirstOrDefaultAsync(ct);
        if (product is null)
            return Render("Error", new InertiaProps { ["status"] = 404 }).WithStatusCode(404);

        // Verified purchase: a delivered order containing this product.
        Guid? purchaseItem = await db.OrderItems
            .Where(i => i.ProductId == productId
                && i.Order.UserId == uid
                && i.Order.Status == OrderStatuses.Delivered)
            .OrderByDescending(i => i.Order.DeliveredAt)
            .Select(i => (Guid?)i.Id)
            .FirstOrDefaultAsync(ct);
        if (purchaseItem is null)
            return Back().WithFlash("toast", new Toast("error", "Reviews open once your delivery of this item completes."));

        var existing = await db.Reviews.FirstOrDefaultAsync(r => r.ProductId == productId && r.UserId == uid, ct);
        var now = DateTime.UtcNow;
        if (existing is { } review)
        {
            review.Rating = rating;
            review.Title = title?.Trim();
            review.Body = body.Trim();
            review.OrderItemId ??= purchaseItem;
            review.UpdatedAt = now;
        }
        else
        {
            db.Reviews.Add(new Review
            {
                ProductId = productId,
                UserId = uid,
                OrderItemId = orderItemId ?? purchaseItem,
                Rating = rating,
                Title = title?.Trim(),
                Body = body.Trim(),
            });
        }
        await db.SaveChangesAsync(ct);
        await RecountAsync(db, productId, ct);
        return Back().WithFlash("toast", new Toast("success", existing is null ? "Review published." : "Review updated."));
    }

    /// One review per user per product drives upserts through POST here too.
    internal static async Task RecountAsync(AppDb db, Guid productId, CancellationToken ct)
    {
        var agg = await db.Reviews.AsNoTracking()
            .Where(r => r.ProductId == productId && r.Status == ReviewStatuses.Published)
            .Select(r => new { Avg = r.Rating, Count = 1 })
            .ToListAsync(ct);
        await db.Products.IgnoreQueryFilters()
            .Where(p => p.Id == productId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(p => p.RatingAvg, agg.Count == 0
                    ? 0m : Math.Round((decimal)agg.Sum(x => x.Avg) / agg.Count, 2, MidpointRounding.AwayFromZero))
                .SetProperty(p => p.RatingCount, agg.Count), ct);
    }

    // ---------------------------------------------------------------- admin moderation

    internal static async Task<IResult> AdminAsync(string? state, int? page, HttpContext ctx, AppDb db, CancellationToken ct)
    {
        var pageNo = Math.Max(page ?? 1, 1);
        var size = 25;
        var query = db.Reviews.AsNoTracking().IgnoreQueryFilters();
        var hidden = state == "hidden";
        query = query.Where(r => hidden ? r.Status == ReviewStatuses.Hidden : true);
        var rows = await query
            .OrderByDescending(r => r.CreatedAt).ThenByDescending(r => r.Id)
            .Skip((pageNo - 1) * size).Take(size + 1)
            .Select(r => new
            {
                r.Id,
                Product = r.Product.Name,
                Slug = r.Product.Slug,
                Author = r.User.Name,
                r.Rating,
                r.Title,
                r.Body,
                r.Status,
                r.CreatedAt,
            })
            .ToListAsync(ct);
        return Render(
            "admin/Reviews",
            new InertiaProps
            {
                ["reviews"] = new AdminReviewsPage(rows.Take(size).Select(r => (object)r).ToList(), pageNo, rows.Count > size),
                ["state"] = hidden ? "hidden" : "published",
            }
        );
    }

    internal static async Task<IResult> HideAsync(Guid id, string? state, HttpContext ctx, AppDb db, CancellationToken ct)
    {
        var review = await db.Reviews.IgnoreQueryFilters().AsNoTracking()
            .Where(r => r.Id == id)
            .Select(r => new { r.Id, r.ProductId, r.Status })
            .FirstOrDefaultAsync(ct);
        if (review is null)
            return Render("Error", new InertiaProps { ["status"] = 404 }).WithStatusCode(404);
        var newStatus = state == "unhide" ? ReviewStatuses.Published : ReviewStatuses.Hidden;
        if (newStatus == review.Status) return Back();
        await db.Reviews.Where(r => r.Id == id)
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.Status, newStatus), ct);
        await RecountAsync(db, review.ProductId, ct);
        ctx.Inertia().Flash("toast", new Toast("success", newStatus == ReviewStatuses.Hidden ? "Review hidden." : "Review restored."));
        return Back();
    }
}

public sealed record AdminReviewsPage(IReadOnlyList<object> Data, int Page, bool HasMore) : IProvidesScrollMetadata
{
    public ScrollMetadata GetScrollMetadata() => ScrollMetadata.FromPage(Page, HasMore);
}
