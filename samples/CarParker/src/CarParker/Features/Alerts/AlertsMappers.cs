namespace CarParker.Features.Alerts;

public static class AlertsMappers
{
    public static NotificationRow Row(Alert alert) => new(alert.Id, alert.Kind, alert.Title,
        alert.Kind switch
        {
            "reservation_soon" when alert.Body?.StartsWith("occurrence:", StringComparison.Ordinal) == true => "Your parking window is coming up. Open your booking for arrival details.",
            "overstay" when alert.Body?.StartsWith("session:", StringComparison.Ordinal) == true => "Your vehicle is still on site after its booked window. Please contact the gate team.",
            _ => alert.Body,
        }, alert.Href, alert.At, alert.ReadAt);
}
