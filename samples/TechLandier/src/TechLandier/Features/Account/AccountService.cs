using Microsoft.EntityFrameworkCore;
using TechLandier.Data;

namespace TechLandier.Features;

public sealed record ProfileInput(string? Name, Guid? AvatarAssetId);

public sealed record PreferencesInput(
    string? Theme,
    bool EmailOrderUpdates = true,
    bool EmailInstallmentReminders = true,
    bool EmailSupportReplies = true,
    bool EmailPromos = false,
    string? TimeZoneId = null);

public sealed record AccountStats(int Orders, int ActivePlans, int ActiveWarranties, int OpenRepairs);
public sealed record RecentOrder(Guid Id, string Number, string Status, decimal Total, DateTime CreatedAt);
public sealed record NextInstallment(Guid Id, decimal Amount, DateOnly DueDate, string? Plan);
public sealed record OpenDelivery(Guid Id, string OrderNumber, string Status, string? Rider);
public sealed record UnreadAlert(Guid Id, string Kind, string Title, string Body, string? Href, DateTime CreatedAt);
public sealed record ProfileData(string Name, string Email, Guid? AvatarAssetId);

/// Customer dashboard widgets, profile and notification preferences. The widget loaders run as parallel
/// Inertia props, so they use their own contexts.
public sealed class AccountService(AppDb db, IDbContextFactory<AppDb> dbs)
{
    static readonly string[] Themes = ["system", "light", "dark"];

    public async Task<AccountStats> StatsAsync(Guid uid, CancellationToken ct)
    {
        await using var own = await dbs.CreateDbContextAsync(ct);
        return await own.OneRow.Select(_ => new AccountStats(
                own.Orders.Count(o => o.UserId == uid),
                own.PayPlans.Count(p => p.UserId == uid && p.Status == PayPlanStatuses.Active),
                own.Warranties.Count(w => w.UserId == uid && w.Status == WarrantyStatuses.Active),
                own.RepairTickets.Count(r => r.UserId == uid && r.Status != RepairStatuses.Closed && r.Status != RepairStatuses.PickedUp)))
            .FirstAsync(ct);
    }

    public async Task<List<RecentOrder>> RecentOrdersAsync(Guid uid, CancellationToken ct)
    {
        await using var own = await dbs.CreateDbContextAsync(ct);
        return await own.Orders.AsNoTracking()
            .Where(o => o.UserId == uid)
            .OrderByDescending(o => o.CreatedAt).ThenByDescending(o => o.Id)
            .Take(5)
            .Select(o => new RecentOrder(o.Id, o.Number, o.Status, o.Total, o.CreatedAt))
            .ToListAsync(ct);
    }

    public async Task<NextInstallment?> NextInstallmentAsync(Guid uid, CancellationToken ct)
    {
        await using var own = await dbs.CreateDbContextAsync(ct);
        return await own.Installments.AsNoTracking()
            .Where(i => i.Plan.UserId == uid && i.Status == InstallmentStatuses.Due)
            .OrderBy(i => i.DueDate)
            .Select(i => new NextInstallment(i.Id, i.Amount, i.DueDate, i.Plan.Order.Number))
            .FirstOrDefaultAsync(ct);
    }

    public async Task<List<OpenDelivery>> OpenDeliveriesAsync(Guid uid, CancellationToken ct)
    {
        await using var own = await dbs.CreateDbContextAsync(ct);
        return await own.Deliveries.AsNoTracking()
            .Where(d => d.Order.UserId == uid && d.Status != DeliveryStatuses.Delivered && d.Order.Status != OrderStatuses.Cancelled)
            .OrderBy(d => d.CreatedAt)
            .Take(3)
            .Select(d => new OpenDelivery(d.Id, d.Order.Number, d.Status, d.Rider != null ? d.Rider.Name : null))
            .ToListAsync(ct);
    }

    public async Task<List<UnreadAlert>> UnreadAlertsAsync(Guid uid, CancellationToken ct)
    {
        await using var own = await dbs.CreateDbContextAsync(ct);
        return await own.Alerts.AsNoTracking()
            .Where(a => a.UserId == uid && a.ReadAt == null)
            .OrderByDescending(a => a.CreatedAt).ThenByDescending(a => a.Id)
            .Take(4)
            .Select(a => new UnreadAlert(a.Id, a.Kind, a.Title, a.Body, a.Href, a.CreatedAt))
            .ToListAsync(ct);
    }

    public async Task<(ProfileData Profile, UserPreferences Preferences)> ProfileAsync(Guid uid, CancellationToken ct)
    {
        var user = await db.Users.AsNoTracking().FirstAsync(u => u.Id == uid, ct);
        return (new ProfileData(user.Name, user.Email, user.AvatarAssetId), user.Preferences);
    }

    public async Task<SaveResult> SaveProfileAsync(Guid uid, ProfileInput input, CancellationToken ct)
    {
        if (input.Name?.Trim() is not { Length: >= 2 } name) return SaveResult.Invalid("name", "Name needs at least 2 characters.");
        if (input.AvatarAssetId is { } assetId
            && !await db.Assets.AnyAsync(a => a.Id == assetId && a.OwnerId == uid && a.Status == AssetStatus.Ready && a.ContentType.StartsWith("image/"), ct))
            return SaveResult.Invalid("avatarAssetId", "Upload an image first.");

        await db.Users.Where(u => u.Id == uid)
            .ExecuteUpdateAsync(s => s.SetProperty(u => u.Name, name).SetProperty(u => u.AvatarAssetId, input.AvatarAssetId), ct);
        return SaveResult.Success("Profile updated.");
    }

    public async Task<SaveResult> SavePreferencesAsync(Guid uid, PreferencesInput input, CancellationToken ct)
    {
        var theme = input.Theme ?? "system";
        var zone = input.TimeZoneId ?? "UTC";
        if (!Themes.Contains(theme)) return SaveResult.Invalid("theme", "Choose system, light or dark.");
        if (!TimeZoneInfo.TryFindSystemTimeZoneById(zone, out _)) return SaveResult.Invalid("timeZoneId", "Choose a valid IANA time zone.");
        var user = await db.Users.FirstAsync(u => u.Id == uid, ct);
        user.Preferences = new UserPreferences
        {
            Theme = theme,
            EmailOrderUpdates = input.EmailOrderUpdates,
            EmailInstallmentReminders = input.EmailInstallmentReminders,
            EmailSupportReplies = input.EmailSupportReplies,
            EmailPromos = input.EmailPromos,
            TimeZoneId = zone,
        };
        await db.SaveChangesAsync(ct);
        return SaveResult.Success("Preferences saved.");
    }
}
