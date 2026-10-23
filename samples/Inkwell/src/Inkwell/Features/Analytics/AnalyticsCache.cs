namespace Inkwell.Features.Analytics;

/// <summary>
/// The cache policy shared by the analytics props and the posts that invalidate them. A dashboard stays fresh for a
/// minute, then its last value is served while one request refreshes it (stale-while-revalidate). Post create, update
/// and delete remove the author's entries at once; views, likes and comments stay eventually consistent within the
/// fresh/stale window, which is what the deferred dashboard already tolerated.
/// </summary>
public static class AnalyticsCache
{
    public static readonly TimeSpan FreshFor = TimeSpan.FromSeconds(60);
    public static readonly TimeSpan StaleFor = TimeSpan.FromSeconds(120);

    public static string UserTag(Guid userId) => $"analytics:user:{userId}";
    public static string PostTag(Guid postId) => $"analytics:post:{postId}";
}
