using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace SvelteMongoBlog.Features;

public static partial class PostsHelpers
{
    internal static readonly HashSet<string> Reserved = ["create", "edit", "new"];

    internal static IResult CoverError() =>
        Back()
            .WithErrors(
                new Dictionary<string, string> { ["coverAssetId"] = "Upload an image first." }
            );

    [GeneratedRegex(@"[^a-z0-9]+")]
    private static partial Regex NonSlugRx();

    [GeneratedRegex(@"!\[[^\]]*\]\([^)]*\)")]
    private static partial Regex ImageRx();

    [GeneratedRegex(@"\[([^\]]*)\]\([^)]*\)")]
    private static partial Regex LinkRx();

    [GeneratedRegex(@"[#*_`>~]+")]
    private static partial Regex MdCharsRx();

    [GeneratedRegex(@"\s+")]
    private static partial Regex SpaceRx();

    internal static string Slugify(string title)
    {
        var sb = new StringBuilder();
        foreach (var c in title.Normalize(NormalizationForm.FormD))
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                sb.Append(char.ToLowerInvariant(c));
        var slug = NonSlugRx().Replace(sb.ToString(), "-").Trim('-');
        if (slug.Length > 80)
            slug = slug[..80].Trim('-');
        return slug.Length == 0 ? "post" : slug;
    }

    internal static string RandomSuffix() =>
        string.Create(
            6,
            0,
            static (span, _) =>
            {
                const string chars = "abcdefghijklmnopqrstuvwxyz0123456789";
                for (var i = 0; i < span.Length; i++)
                    span[i] = chars[Random.Shared.Next(chars.Length)];
            }
        );

    internal static string MakeExcerpt(string body)
    {
        var s = ImageRx().Replace(body, "");
        s = LinkRx().Replace(s, "$1");
        s = SpaceRx().Replace(MdCharsRx().Replace(s, ""), " ").Trim();
        return s.Length <= 200 ? s : s[..200].TrimEnd() + "â€¦";
    }
}
