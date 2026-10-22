using System.ComponentModel.DataAnnotations;
using MongoDB.Driver;
using CarParker.Data;
using CarParker.Infrastructure;

namespace CarParker.Features;

public sealed class PreferencesInput
{
    [Required, AllowedValues("system", "light", "dark")] public string? Theme { get; set; }
    public bool BookingReminders { get; set; }
    public bool EmailOnCheckout { get; set; }
    public bool EmailOnRefund { get; set; }
}

public sealed class ProfileInput
{
    [Required, StringLength(80, MinimumLength = 2)] public string? Name { get; set; }
    [StringLength(24)] public string? Phone { get; set; }
    public Guid? AvatarAssetId { get; set; }
}
