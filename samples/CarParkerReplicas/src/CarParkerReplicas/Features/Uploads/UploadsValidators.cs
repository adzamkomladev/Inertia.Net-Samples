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
        ["application/pdf"] = (".pdf", 20 * Mb),
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
