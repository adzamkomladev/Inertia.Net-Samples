namespace CarParker.Features.Bookings;

/// <summary>Cancellation refund matrix (pure): ≥24h before start → full, ≥4h → 50%, <4h or started → none.</summary>
public static class RefundRules
{
    public const string Full = "24h_full";
    public const string Half = "4h_half";
    public const string None = "late_none";
    public const string PolicyPayout = "policy_payout";

    public static (string Rule, long Cents) ComputeCents(DateTime? startAtUtc, long totalCents, DateTime now)
    {
        if (startAtUtc is not { } start) return (None, 0);
        var lead = start - now;
        if (lead >= TimeSpan.FromHours(24)) return (Full, totalCents);
        if (lead >= TimeSpan.FromHours(4)) return (Half, totalCents / 2);
        return (None, 0);
    }
}

/// <summary>Pricing helpers: all money is cents-long (`RateCents`/`TotalCents`); "$" is applied on the client only.</summary>
public static class PricingRules
{
    public const int InsurancePremiumMinCents = 200;
    public const int InsurancePercent = 12;
    public const int MaxRecurringWeeks = 8;

    /// <summary>ceil($rate × hours, min 1h) capped by DailyMax per 24h slice from the window start.
    /// Whole minutes are priced, sub-minute tails are dropped — the customer never pays three hours
    /// for a 2h+30µs window.</summary>
    public static long WindowCents(long ratePerHourCents, long dailyMaxCents, DateTime startUtc, DateTime endUtc)
    {
        if (startUtc.Kind is not DateTimeKind.Utc) startUtc = DateTime.SpecifyKind(startUtc, DateTimeKind.Utc);
        if (endUtc.Kind is not DateTimeKind.Utc) endUtc = DateTime.SpecifyKind(endUtc, DateTimeKind.Utc);
        var minutes = Math.Floor(Math.Max(0, (endUtc - startUtc).TotalSeconds) / 60.0);
        var hours = Math.Max(1, (long)Math.Ceiling(minutes / 60.0));
        long total = 0;
        while (hours > 0)
        {
            var slice = Math.Min(24, hours);
            var sliceCents = slice * ratePerHourCents;
            if (sliceCents > dailyMaxCents && dailyMaxCents > 0) sliceCents = dailyMaxCents;
            total += sliceCents;
            hours -= slice;
        }
        return total;
    }

    /// <summary>12% of the booking, minimum $2.</summary>
    public static long InsurancePremiumCents(long bookingCents)
    {
        var premium = bookingCents * InsurancePercent / 100;
        return Math.Max(InsurancePremiumMinCents, premium);
    }
}
