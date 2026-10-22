
using Microsoft.EntityFrameworkCore;
using TechLandier.Features;

namespace TechLandier.Data;

public sealed class User
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public required string Email { get; set; }
    public required string Name { get; set; }
    /// Role claim ("customer" | "admin"); seeded admins only, public registration is always a customer.
    public string Role { get; set; } = "customer";
    public string PasswordHash { get; set; } = "";
    public DateTime? EmailVerifiedAt { get; set; }
    public Guid? AvatarAssetId { get; set; }
    public UserPreferences Preferences { get; set; } = new();
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public bool IsDemo { get; set; }
    public string? DemoKey { get; set; }
}

public sealed class UserPreferences
{
    public string Theme { get; set; } = "system"; // system | light | dark
    public bool EmailOrderUpdates { get; set; } = true;
    public bool EmailInstallmentReminders { get; set; } = true;
    public bool EmailSupportReplies { get; set; } = true;
    public bool EmailPromos { get; set; } = false;
    public string TimeZoneId { get; set; } = "UTC";
}
