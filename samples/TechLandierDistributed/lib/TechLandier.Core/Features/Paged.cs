using Microsoft.EntityFrameworkCore;

namespace TechLandier.Features;

/// One page of a list; the extra row fetched past the page size tells whether more follow.
/// MainApp converts it to Inertia scroll metadata at the route boundary.
public sealed record Paged<T>(List<T> Data, int Page, bool HasMore);

/// A numbered page with totals, for lists a person pages through by number (admin care queues, delivery history).
/// Serializes to the client's NumberedPage shape; Core stays free of the Inertia.Net page type.
public sealed record NumberedPage<T>(List<T> Data, int CurrentPage, int PerPage, int Total, int LastPage, int From, int To, bool HasMore, string PageName = "page")
{
    public static NumberedPage<T> Empty(int perPage) => new([], 1, perPage, 0, 1, 0, 0, false);
}

public static class PagedQuery
{
    public static async Task<Paged<T>> ToPagedAsync<T>(this IQueryable<T> query, int? page, int size, CancellationToken ct)
    {
        var pageNo = Math.Max(page ?? 1, 1);
        var rows = await query.Skip((pageNo - 1) * size).Take(size + 1).ToListAsync(ct);
        return new Paged<T>(rows.Take(size).ToList(), pageNo, rows.Count > size);
    }

    public static async Task<NumberedPage<T>> ToNumberedPageAsync<T>(this IQueryable<T> query, int? page, int size, CancellationToken ct)
    {
        var total = await query.CountAsync(ct);
        var lastPage = Math.Max((int)Math.Ceiling(total / (double)size), 1);
        var pageNo = Math.Clamp(page ?? 1, 1, lastPage);
        var rows = await query.Skip((pageNo - 1) * size).Take(size).ToListAsync(ct);
        var from = rows.Count == 0 ? 0 : (pageNo - 1) * size + 1;
        return new NumberedPage<T>(rows, pageNo, size, total, lastPage, from, rows.Count == 0 ? 0 : from + rows.Count - 1, pageNo < lastPage);
    }
}
