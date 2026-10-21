using Microsoft.EntityFrameworkCore;
using VuePgBlog.Data;
using VuePgBlog.Infrastructure;

namespace VuePgBlog.Features;

/// Customer dashboard: overview rail, profile + notification preferences (folds the old Settings pages in).
public static partial class AccountService
{
    internal static async Task<IResult> OverviewAsync(HttpContext ctx, AppDb db, CancellationToken ct)
    {
        var uid = ctx.User.UserId();
        var now = DateOnly.FromDateTime(DateTime.UtcNow);
        return Render(
            "account/Index",
            new InertiaProps
            {
                ["stats"] = new
                {
                    Orders = await db.Orders.CountAsync(o => o.UserId == uid, ct),
                    ActivePlans = await db.PayPlans.CountAsync(p => p.UserId == uid && p.Status == PayPlanStatuses.Active, ct),
                    ActiveWarranties = await db.Warranties.CountAsync(w => w.UserId == uid && w.Status == WarrantyStatuses.Active, ct),
                    OpenRepairs = await db.RepairTickets.CountAsync(r => r.UserId == uid
                        && r.Status != RepairStatuses.Closed && r.Status != RepairStatuses.PickedUp, ct),
                },
                ["recentOrders"] = await db.Orders.AsNoTracking()
                    .Where(o => o.UserId == uid)
                    .OrderByDescending(o => o.CreatedAt).ThenByDescending(o => o.Id)
                    .Take(5)
                    .Select(o => new { o.Id, o.Number, o.Status, o.Total, o.CreatedAt })
                    .ToListAsync(ct),
                ["nextInstallment"] = await db.Installments.AsNoTracking()
                    .Where(i => i.Plan.UserId == uid && i.Status == InstallmentStatuses.Due)
                    .OrderBy(i => i.DueDate)
                    .Select(i => new { i.Id, i.Amount, i.DueDate, Plan = i.Plan.Order != null ? i.Plan.Order.Number : null })
                    .FirstOrDefaultAsync(ct),
                ["openDeliveries"] = await db.Deliveries.AsNoTracking()
                    .Where(d => d.Order.UserId == uid && d.Status != DeliveryStatuses.Delivered && d.Order.Status != OrderStatuses.Cancelled)
                    .OrderBy(d => d.CreatedAt)
                    .Take(3)
                    .Select(d => new { d.Id, OrderNumber = d.Order.Number, d.Status, Rider = d.Rider != null ? d.Rider.Name : null })
                    .ToListAsync(ct),
                ["unreadAlerts"] = await db.Alerts.AsNoTracking()
                    .Where(a => a.UserId == uid && a.ReadAt == null)
                    .OrderByDescending(a => a.CreatedAt).ThenByDescending(a => a.Id)
                    .Take(4)
                    .Select(a => new { a.Id, a.Kind, a.Title, a.Body, a.Href, a.CreatedAt })
                    .ToListAsync(ct),
            }
        );
    }

    internal static async Task<IResult> ProfileAsync(HttpContext ctx, AppDb db, CancellationToken ct)
    {
        var user = await db.Users.AsNoTracking().FirstAsync(u => u.Id == ctx.User.UserId(), ct);
        return Render(
            "account/Preferences",
            new InertiaProps
            {
                ["profile"] = new { name = user.Name, email = user.Email, avatarAssetId = user.AvatarAssetId },
                ["preferences"] = user.Preferences,
            }
        );
    }

    internal static async Task<IResult> UpdateProfileAsync(ProfileInput input, HttpContext ctx, AppDb db, CancellationToken ct)
    {
        var userId = ctx.User.UserId();
        if (string.IsNullOrWhiteSpace(input.Name) || input.Name.Trim().Length < 2)
            return Back().WithErrors(new Dictionary<string, string> { ["name"] = "Name needs at least 2 characters." });
        if (
            input.AvatarAssetId is { } assetId
            && !await db.Assets.AnyAsync(
                a => a.Id == assetId && a.OwnerId == userId && a.Status == AssetStatus.Ready && a.ContentType.StartsWith("image/"),
                ct)
        )
            return Back().WithErrors(new Dictionary<string, string> { ["avatarAssetId"] = "Upload an image first." });

        await db.Users.Where(u => u.Id == userId)
            .ExecuteUpdateAsync(
                s => s.SetProperty(u => u.Name, input.Name!.Trim())
                    .SetProperty(u => u.AvatarAssetId, input.AvatarAssetId),
                ct);
        return Back().WithFlash("toast", new Toast("success", "Profile updated."));
    }

    internal static async Task<IResult> UpdatePreferencesAsync(PreferencesInput input, HttpContext ctx, AppDb db, CancellationToken ct)
    {
        if (!TimeZoneInfo.TryFindSystemTimeZoneById(input.TimeZoneId ?? "UTC", out _))
            return Back().WithErrors(new Dictionary<string, string> { ["timeZoneId"] = "Choose a valid IANA time zone." });
        var user = await db.Users.FirstAsync(u => u.Id == ctx.User.UserId(), ct);
        user.Preferences = new UserPreferences
        {
            Theme = input.Theme ?? "system",
            EmailOrderUpdates = input.EmailOrderUpdates,
            EmailInstallmentReminders = input.EmailInstallmentReminders,
            EmailSupportReplies = input.EmailSupportReplies,
            EmailPromos = input.EmailPromos,
            TimeZoneId = input.TimeZoneId ?? "UTC",
        };
        await db.SaveChangesAsync(ct);
        // Read by the inline script in app.html before first paint, so SSR pages don't flash the wrong theme.
        ctx.Response.Cookies.Append(
            "theme",
            user.Preferences.Theme,
            new CookieOptions { MaxAge = TimeSpan.FromDays(365), SameSite = SameSiteMode.Lax, Path = "/" });
        return Back().WithFlash("toast", new Toast("success", "Preferences saved."));
    }
}

public sealed record ProfileInput(string? Name, Guid? AvatarAssetId);

public sealed record PreferencesInput(
    string? Theme,
    bool EmailOrderUpdates = true,
    bool EmailInstallmentReminders = true,
    bool EmailSupportReplies = true,
    bool EmailPromos = false,
    string? TimeZoneId = null);
