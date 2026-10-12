using System.Globalization;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using VuePgReplicasAdvBlog.Data;
using VuePgReplicasAdvBlog.Features.Uploads;
using static VuePgReplicasAdvBlog.Features.Posts.PostsService;

namespace VuePgReplicasAdvBlog.Features.Posts;

public static partial class PostsHelpers
{
    internal static PostSummary ToSummary(Row r, string? snippet, Storage storage) =>
        new(
            r.Id,
            r.Slug,
            r.Title,
            r.Excerpt,
            snippet,
            r.CoverKey is null ? null : storage.Url(r.CoverKey),
            new AuthorDto(
                r.AuthorId,
                r.AuthorName,
                r.AvatarKey is null ? null : storage.Url(r.AvatarKey)
            ),
            r.LikeCount,
            r.CommentCount,
            r.CreatedAt
        );

    internal // Literal segments under /posts/ can never be slugs.
    static readonly HashSet<string> Reserved = ["create", "edit", "new"];

    // ---- search ----
    [GeneratedRegex(@"[\p{L}\p{N}]+")]
    private static partial Regex WordRx();

    internal
    /// Quoted tokens (no FTS operator injection), prefix match on the last one. Null when q has no tokens.
    static string? BuildMatch(string? q)
    {
        if (string.IsNullOrWhiteSpace(q))
            return null;
        var tokens = WordRx()
            .Matches(q)
            .Select(m => m.Value.Replace("\"", "\"\""))
            .Take(8)
            .ToList();
        if (tokens.Count == 0)
            return null;
        return string.Join(" & ", tokens.Select(t => t + ":*"));
    }

    internal static string EncodeSnippet(string? s) =>
        WebUtility
            .HtmlEncode(s ?? "")
            .Replace("&lt;mark&gt;", "<mark>")
            .Replace("&lt;/mark&gt;", "</mark>");

    // ---- slug / excerpt ----
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

    public static string MakeExcerpt(string body)
    {
        var s = ImageRx().Replace(body, "");
        s = LinkRx().Replace(s, "$1");
        s = SpaceRx().Replace(MdCharsRx().Replace(s, ""), " ").Trim();
        return s.Length <= 200 ? s : s[..200].TrimEnd() + "…";
    }
    [GeneratedRegex("^[a-z0-9]+(?:-[a-z0-9]+)*$")]
    private static partial Regex TopicPattern();

    public static string[] NormalizeTopics(string[]? topics) =>
        (topics ?? []).Select(t => (t ?? "").Trim().ToLowerInvariant()).Distinct().ToArray();

    public static string? TopicsError(string[]? topics)
    {
        var normalized = NormalizeTopics(topics);
        return
            normalized.Length > 5
            || normalized.Any(t => t.Length is 0 or > 32 || !TopicPattern().IsMatch(t))
            ? "Use up to five topics of 1–32 lowercase letters, numbers or hyphens."
            : null;
    }
}
