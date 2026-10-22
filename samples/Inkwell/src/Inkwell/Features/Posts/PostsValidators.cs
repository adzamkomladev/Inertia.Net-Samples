using System.Text.RegularExpressions;

namespace Inkwell.Features.Posts;

public static partial class PostsValidators
{
    public static string[] NormalizeTopics(string[]? topics) =>
        (topics ?? []).Select(t => (t ?? "").Trim().ToLowerInvariant()).Distinct().ToArray();

    public static bool TopicsValid(string[] normalized) =>
        normalized.Length <= 5 && normalized.All(t => t.Length is > 0 and <= 32 && TopicPattern().IsMatch(t));

    [GeneratedRegex("^[a-z0-9]+(?:-[a-z0-9]+)*$")]
    private static partial Regex TopicPattern();

}
