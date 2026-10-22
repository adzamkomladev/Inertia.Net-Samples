namespace TechLandier.Features;

public sealed class StoreSettings
{
    public int Id { get; set; } = 1;
    public string Name { get; set; } = "TechLandier";
    public string Email { get; set; } = "support@yopmail.com";
    public string Phone { get; set; } = "";
    public string Address { get; set; } = "";
    public string TimeZoneId { get; set; } = "Africa/Accra";
    public string HoursJson { get; set; } = "[]";
}
public sealed record BusinessHours(int Day, bool Closed, string Opens, string Closes, bool DeliveryClosed, string DeliveryOpens, string DeliveryCloses);
public sealed record StoreSettingsView(string Name, string Email, string Phone, string Address, string TimeZoneId, List<BusinessHours> Hours);
public sealed record TeamMember(Guid Id, string Name, string Email, string Role);
public sealed record TeamInput(string? Name, string? Email, string? Password);
