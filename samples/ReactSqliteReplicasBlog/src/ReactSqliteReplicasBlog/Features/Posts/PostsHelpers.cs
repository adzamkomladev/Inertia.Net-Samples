using System.Net;
using System.Text.RegularExpressions;

namespace ReactSqliteReplicasBlog.Features.Posts;

public static partial class PostsHelpers
{
    internal const int PageSize = 10;

    internal static string? Clean(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}

public static partial class Search
{
    /// <summary>Turns user input into a safe FTS5 query: each token quoted with a prefix star, AND-ed. Null when empty.</summary>
    public static string? ToFtsQuery(string? q)
    {
        if (string.IsNullOrWhiteSpace(q))
            return null;
        var tokens = q.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
            .Select(t => new string(
                t.Where(c => char.IsLetterOrDigit(c) || c is '_' or '-').ToArray()
            ))
            .Where(t => t.Length > 0)
            .Select(t => $"\"{t}\"*");
        var query = string.Join(' ', tokens);
        return query.Length == 0 ? null : query;
    }

    /// <summary>HTML-encodes an FTS snippet, then restores only the &lt;mark&gt; tags SQLite added.</summary>
    public static string SafeSnippet(string snippet) =>
        WebUtility
            .HtmlEncode(snippet)
            .Replace("&lt;mark&gt;", "<mark>")
            .Replace("&lt;/mark&gt;", "</mark>");

    /// <summary>First ~200 characters of a markdown body with syntax stripped crudely.</summary>
    public static string Excerpt(string markdown)
    {
        var s = Image().Replace(markdown, "");
        s = Link().Replace(s, "$1");
        s = Syntax().Replace(s, "");
        s = Spaces().Replace(s, " ").Trim();
        return s.Length <= 200 ? s : s[..200].TrimEnd() + "…";
    }

    [GeneratedRegex(@"!\[[^\]]*\]\([^)]*\)")]
    private static partial Regex Image();

    [GeneratedRegex(@"\[([^\]]*)\]\([^)]*\)")]
    private static partial Regex Link();

    [GeneratedRegex(@"(?m)^\s{0,3}(#{1,6}|>|[-*+]|\d+\.)\s+|[`*_~]|^---+$")]
    private static partial Regex Syntax();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Spaces();

    [GeneratedRegex(@"\]\((\S+?)\)")]
    public static partial Regex MarkdownUrl();
}
