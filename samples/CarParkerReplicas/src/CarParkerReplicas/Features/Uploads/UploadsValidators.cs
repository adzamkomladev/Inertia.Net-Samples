using System.ComponentModel.DataAnnotations;

namespace CarParker.Features;

internal static class UploadsValidators
{
    public const long Mb = 1024 * 1024;

    public static readonly Dictionary<string, (string Ext, long Max)> Allowed = new(StringComparer.OrdinalIgnoreCase)
    {
        ["image/png"] = (".png", 5 * Mb),
        ["image/jpeg"] = (".jpg", 5 * Mb),
        ["image/webp"] = (".webp", 5 * Mb),
        ["image/gif"] = (".gif", 5 * Mb),
    };

    /// Whether the leading bytes are the signature the declared image type promises.
    public static bool MatchesSignature(string contentType, ReadOnlySpan<byte> head) => contentType.ToLowerInvariant() switch
    {
        "image/png" => head.StartsWith(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }),
        "image/jpeg" => head.StartsWith(new byte[] { 0xFF, 0xD8, 0xFF }),
        "image/gif" => head.StartsWith("GIF87a"u8) || head.StartsWith("GIF89a"u8),
        "image/webp" => head.Length >= 12 && head[..4].SequenceEqual("RIFF"u8) && head.Slice(8, 4).SequenceEqual("WEBP"u8),
        _ => false,
    };

    public static Dictionary<string, string[]> Problem(string field, string message) => new() { [field] = [message] };

    public static Dictionary<string, string[]>? Validate(PresignInput input)
    {
        var results = new List<ValidationResult>();
        if (!Validator.TryValidateObject(input, new ValidationContext(input), results, true))
            return results
                .SelectMany(r => (r.MemberNames.Any() ? r.MemberNames : [""]).Select(m => (m, r.ErrorMessage ?? "Invalid.")))
                .GroupBy(x => x.m).ToDictionary(g => g.Key, g => g.Select(x => x.Item2).ToArray());
        return null;
    }
}
