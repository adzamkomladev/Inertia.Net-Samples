using System.Text.RegularExpressions;
using CarParker.Data;

namespace CarParker.Features;

public static partial class PreferencesValidators
{
    public const int MaxVehicles = 5;

    [GeneratedRegex(@"^[A-Z0-9 \-]{2,16}$")] private static partial Regex Plate();

    static string PlateOf(VehicleInput v) => (v.Plate ?? "").Trim().ToUpperInvariant();

    /// <summary>The list-level rules DataAnnotations cannot express: size, plate shape and duplicates.</summary>
    public static string? VehiclesError(VehiclesInput input)
    {
        if (input.Vehicles is not { } vehicles) return "Send the vehicle list.";
        if (vehicles.Any(v => v is null)) return "Every vehicle needs a plate.";
        if (vehicles.Count > MaxVehicles) return $"Save up to {MaxVehicles} vehicles.";
        var plates = vehicles.Select(v => PlateOf(v!)).ToArray();
        if (plates.Any(p => !Plate().IsMatch(p))) return "Plates use 2–16 letters, numbers, spaces or dashes.";
        if (plates.Distinct().Count() != plates.Length) return "That plate is already saved.";
        if (vehicles.Any(v => v!.Label?.Trim().Length > 40)) return "Keep nicknames under 40 characters.";
        return null;
    }

    /// <summary>Uppercases plates and keeps exactly one default (the first flagged, else the first vehicle). Call after <see cref="VehiclesError"/>.</summary>
    public static List<SavedVehicle> Normalize(VehiclesInput input)
    {
        var vehicles = input.Vehicles!.Select(v => v!).ToList();
        var defaultIndex = Math.Max(0, vehicles.FindIndex(v => v.IsDefault));
        return vehicles.Select((v, i) => new SavedVehicle
        {
            Plate = PlateOf(v),
            Label = string.IsNullOrWhiteSpace(v.Label) ? null : v.Label.Trim(),
            IsDefault = i == defaultIndex,
        }).ToList();
    }
}
