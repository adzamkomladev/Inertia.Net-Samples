using System.ComponentModel.DataAnnotations;
using VuePgReplicasAdvBlog.Data;

namespace VuePgReplicasAdvBlog.Features.Preferences;

public sealed class PreferencesInput
{
    [Required, AllowedValues("system", "light", "dark")]
    public string? Theme { get; set; }
    public bool EmailOnComment { get; set; }
    public bool EmailOnLike { get; set; }
    public bool WeeklyDigest { get; set; }
    public bool DailyRecommendationDigest { get; set; }

    [Required, StringLength(100)]
    public string TimeZoneId { get; set; } = "UTC";

    [Range(5, 50)]
    public int PostsPerPage { get; set; } = 10;
}

public sealed class ProfileInput
{
    [Required, StringLength(80, MinimumLength = 2)]
    public string? Name { get; set; }
    public Guid? AvatarAssetId { get; set; }
}

public sealed record ProfileDetails(string Name, Guid? AvatarAssetId);
public sealed record PreferenceDetails(UserPreferences Preferences, ProfileDetails Profile);
