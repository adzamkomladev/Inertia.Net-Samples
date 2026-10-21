namespace SvelteMongoBlog.Features;

public static partial class UploadsHelpers
{
    internal const long Mb = 1024 * 1024;
    internal // content type -> (extension, max bytes)
    static readonly Dictionary<string, (string Ext, long Max)> Allowed = new(
        StringComparer.OrdinalIgnoreCase
    )
    {
        ["image/png"] = (".png", 5 * Mb),
        ["image/jpeg"] = (".jpg", 5 * Mb),
        ["image/webp"] = (".webp", 5 * Mb),
        ["image/gif"] = (".gif", 5 * Mb),
        ["application/pdf"] = (".pdf", 20 * Mb),
    };

    internal static Dictionary<string, string[]> Problem(string field, string message) =>
        new() { [field] = [message] };
}
