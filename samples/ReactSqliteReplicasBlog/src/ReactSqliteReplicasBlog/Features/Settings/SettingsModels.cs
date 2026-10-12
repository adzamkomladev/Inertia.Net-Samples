using System.ComponentModel.DataAnnotations;

namespace ReactSqliteReplicasBlog.Features.Settings;

public sealed class ProfileInput
{
    [Required, MaxLength(60)]
    public string? DisplayName { get; set; }
    public Guid? AvatarUploadId { get; set; }
    public bool RemoveAvatar { get; set; }
}

public sealed class PreferencesInput
{
    [Required, RegularExpression("^(system|light|dark)$")]
    public string? Theme { get; set; }
    public bool EmailOnComment { get; set; }
    public bool EmailOnLike { get; set; }
    public bool WeeklyDigest { get; set; }
    public bool DailyRecommendationDigest { get; set; }
    public string TimeZoneId { get; set; } = "UTC";
}
