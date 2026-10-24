using System.ComponentModel.DataAnnotations;
using System.Globalization;
namespace TechLandier.Features;
public static class ManagementValidators
{
    public static string? SettingsError(StoreSettingsView input)
    {
        if (string.IsNullOrWhiteSpace(input.Name) || input.Name.Length > 80 || string.IsNullOrWhiteSpace(input.Email) || !new EmailAddressAttribute().IsValid(input.Email)
            || input.Email.Length > 254 || input.Phone is null || input.Phone.Length > 30 || input.Address is null || input.Address.Length > 300)
            return "Enter a store name, valid email, phone (up to 30 characters) and address (up to 300 characters).";
        if (string.IsNullOrWhiteSpace(input.TimeZoneId) || !TimeZoneInfo.TryFindSystemTimeZoneById(input.TimeZoneId, out _)) return "Choose a valid time zone.";
        if (input.Hours is null || input.Hours.Count != 7 || input.Hours.Any(h => h is null) || !input.Hours.Select(h => h.Day).Order().SequenceEqual(Enumerable.Range(0, 7))) return "Provide each day of the week once.";
        foreach (var h in input.Hours)
        {
            if (!h.Closed && !ValidWindow(h.Opens, h.Closes)) return "Opening time must be earlier than closing time (HH:mm).";
            if (!h.DeliveryClosed && !ValidWindow(h.DeliveryOpens, h.DeliveryCloses)) return "Delivery start must be earlier than delivery end (HH:mm).";
        }
        return null;
    }
    static bool ValidWindow(string start, string end) =>
        TimeOnly.TryParseExact(start, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var opens)
        && TimeOnly.TryParseExact(end, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var closes) && opens < closes;
}
