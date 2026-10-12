using VuePgReplicasAdvBlog.Infrastructure;

namespace VuePgReplicasAdvBlog.MainApp.Infrastructure;

public sealed record CursorScrollPage<T>(List<T> Data, string? Cursor, string? NextCursor) : IProvidesScrollMetadata
{
    public ScrollMetadata GetScrollMetadata() => new("cursor", null, NextCursor, Cursor ?? "start");

    public static CursorScrollPage<T> From(CursorPage<T> page) => new(page.Data, page.Cursor, page.NextCursor);
}

public sealed record NumberedScrollPage<T>(List<T> Data, int Page, bool HasMore) : IProvidesScrollMetadata
{
    public ScrollMetadata GetScrollMetadata() => ScrollMetadata.FromPage(Page, HasMore);
}
