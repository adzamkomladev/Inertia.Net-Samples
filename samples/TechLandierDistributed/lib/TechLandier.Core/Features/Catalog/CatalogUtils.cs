namespace TechLandier.Features;

public static class Pricing
{
    public const decimal BnplFeePercent = 5m;
    public const string Currency = "$";
    public const decimal InsurancePremiumPercent = 0.15m;
    public const decimal InsuranceCoveragePercent = 0.90m;

    public static int ClampDiscount(int percent) => Math.Clamp(percent, 0, 70);

    public static decimal Display(decimal price, int discountPercent) =>
        Math.Round(price * (100 - ClampDiscount(discountPercent)) / 100m, 2, MidpointRounding.AwayFromZero);

    public static string Show(decimal amount) =>
        Currency + amount.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture);
}
