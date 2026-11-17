using System.Globalization;

namespace CarParker.Infrastructure;

/// <summary>
/// The CarParker look outside the browser, mirrored from ClientApp/app.css (petrol brand on cool paper, ochre for attention only):
/// emails and PDFs share these hex values because mail clients and PDF engines cannot read oklch.
/// </summary>
public static class BrandPalette
{
    public const string Page = "#f2f6f7", Card = "#fbfdfd", Ink = "#11212b", Muted = "#4d5f68", Line = "#dde6e8", Well = "#eff4f5";
    public const string Brand = "#0e5f70", BrandSoft = "#dcedf0", OnBrand = "#ffffff";
    public const string Ok = "#1e6a42", OkSoft = "#dff1e6", Bad = "#c0342a", BadSoft = "#fbe5e2", Warn = "#7a5200", WarnSoft = "#fbefcf";

    // Dark-mode counterparts (the app's .dark tokens).
    public const string DarkPage = "#12171c", DarkCard = "#1a2128", DarkInk = "#eaf1f2", DarkMuted = "#9fb0b6", DarkLine = "#2a343b", DarkWell = "#222c34";
    public const string DarkBrand = "#5cc8c8", DarkOnBrand = "#08282c", DarkBrandSoft = "#1d3a40";
    public const string DarkOk = "#6fd39a", DarkOkSoft = "#17301f", DarkBad = "#f08a80", DarkBadSoft = "#3a1f1c", DarkWarn = "#e8b53a", DarkWarnSoft = "#38300f";

    public const string Mark = "P";
}

/// <summary>Currency and date text shared by emails and PDFs. The API stores cents; the UI shows US dollars.</summary>
public static class Display
{
    public static string Money(long cents)
    {
        var text = (Math.Abs(cents) / 100m).ToString("N2", CultureInfo.InvariantCulture);
        return cents < 0 ? $"-${text}" : $"${text}";
    }

    public static string Day(DateTime utc) => utc.ToString("d MMM yyyy", CultureInfo.InvariantCulture);

    public static string DateTimeUtc(DateTime utc) => utc.ToString("d MMM yyyy, HH:mm", CultureInfo.InvariantCulture) + " UTC";

    public static string Span(DateTime? start, DateTime? end) =>
        start is { } s && end is { } e ? $"{DateTimeUtc(s)} to {DateTimeUtc(e)}" : "See booking";

    /// IDs are version-7 GUIDs: the first 12 hex digits are a millisecond timestamp, so a prefix collides for records made
    /// close together. The last 12 digits are random, which makes them a safe printed reference (receipts, filenames, emails).
    public static string ShortId(Guid id) => id.ToString("N")[^12..];

    public static string First(string? name) => string.IsNullOrWhiteSpace(name) ? "there" : name.Trim().Split(' ', 2)[0];
}
