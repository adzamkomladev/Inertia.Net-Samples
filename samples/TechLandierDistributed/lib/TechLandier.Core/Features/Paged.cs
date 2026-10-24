using Microsoft.EntityFrameworkCore;

namespace TechLandier.Features;

/// One page of a list; the extra row fetched past the page size tells whether more follow.
/// MainApp converts it to Inertia scroll metadata at the route boundary.
public sealed record Paged<T>(List<T> Data, int Page, bool HasMore);

public static class PagedQuery
{
    public static async Task<Paged<T>> ToPagedAsync<T>(this IQueryable<T> query, int? page, int size, CancellationToken ct)
    {
        var pageNo = Math.Max(page ?? 1, 1);
        var rows = await query.Skip((pageNo - 1) * size).Take(size + 1).ToListAsync(ct);
        return new Paged<T>(rows.Take(size).ToList(), pageNo, rows.Count > size);
    }
}
