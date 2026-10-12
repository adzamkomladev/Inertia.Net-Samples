namespace VuePgReplicasAdvBlog.Infrastructure;

public sealed record CursorPage<T>(List<T> Data, string? Cursor, string? NextCursor);
