using Microsoft.AspNetCore.Identity;

namespace Inkwell.Data;

public sealed class AppUser : IdentityUser<Guid>
{
    public AppUser() => Id = Guid.CreateVersion7();

    public bool IsDemo { get; set; }
    public string? DemoKey { get; set; }
    public DateTime? NextDigestAtUtc { get; set; }
    public string DisplayName { get; set; } = "";
    public string? AvatarUrl { get; set; }
    public DateTime CreatedAt { get; set; }
    public Preferences Preferences { get; set; } = new();
}

/// <summary>Owned by <see cref="AppUser"/>, stored as columns on AspNetUsers.</summary>
public sealed class Preferences
{
    public bool DailyRecommendationDigest { get; set; }
    public string TimeZoneId { get; set; } = "UTC";
    /// <summary>"system", "light" or "dark".</summary>
    public string Theme { get; set; } = "system";
    public bool EmailOnComment { get; set; } = true;
    public bool EmailOnLike { get; set; }
    public bool WeeklyDigest { get; set; } = true;
}
