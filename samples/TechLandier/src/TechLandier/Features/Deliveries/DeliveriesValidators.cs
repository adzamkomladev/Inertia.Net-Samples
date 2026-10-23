namespace TechLandier.Features;

public static class DeliveriesValidators
{
    public const int TextMax = 200;
    public const int ReceivedByMax = 80;

    /// Returns the (field, message) of the first problem with a dispatch text input, or null when it is acceptable.
    public static (string Field, string Message)? TextError(string field, string? value, int max) =>
        value?.Trim().Length > max ? (field, $"Keep it under {max} characters.") : null;

    public static (string Field, string Message)? CapacityError(int? capacity) =>
        capacity is < 1 or > RiderCapacity.Max ? ("capacity", $"Capacity must be between 1 and {RiderCapacity.Max}.") : null;
}
