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
