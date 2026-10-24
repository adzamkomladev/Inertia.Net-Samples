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

public sealed class VehicleInput
{
    [Required, MaxLength(16), RegularExpression(@"^[A-Za-z0-9 \-]{2,16}$")] public string? Plate { get; set; }
    [MaxLength(40)] public string? Label { get; set; }
    public bool IsDefault { get; set; }
}

/// <summary>The whole saved-vehicle list; the page sends it back after each add, remove or default change.
/// The list is required (an empty array clears it) so a malformed body cannot wipe saved vehicles.</summary>
public sealed class VehiclesInput
{
    [Required, MaxLength(PreferencesValidators.MaxVehicles)] public List<VehicleInput?>? Vehicles { get; set; }
}

public sealed record VehicleView(string Plate, string? Label, bool IsDefault);

public sealed class ProfileInput
{
    [Required, StringLength(80, MinimumLength = 2)] public string? Name { get; set; }
    [StringLength(24)] public string? Phone { get; set; }
    public Guid? AvatarAssetId { get; set; }
}
