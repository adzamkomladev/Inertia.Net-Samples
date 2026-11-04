namespace Inkwell.Infrastructure;

/// <summary>Rebuilds library pages after post-query enrichment (likes, snippets, repost attribution).</summary>
public static class Paging
{
    public static CursorPage<TOut> Map<TIn, TOut>(CursorPage<TIn> source, IReadOnlyList<TOut> data) =>
        new(data, source.PerPage, source.NextCursor, source.PreviousCursor, source.HasMore, source.CursorName, source.CurrentCursor);

    public static Page<TOut> Map<TIn, TOut>(Page<TIn> source, IReadOnlyList<TOut> data) =>
        new(data, source.CurrentPage, source.PerPage, source.Total, source.HasMore, source.PageName);
}
