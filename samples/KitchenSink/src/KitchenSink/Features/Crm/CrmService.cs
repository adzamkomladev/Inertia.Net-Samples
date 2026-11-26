using KitchenSink.Data;
using Microsoft.EntityFrameworkCore;

namespace KitchenSink.Features.Crm;

public sealed class CrmService(AppDb db)
{
    public async Task<CursorPage<ContactDto>> ContactsAsync(
        string? search,
        bool favorite,
        string? cursor,
        int? organizationId = null,
        int perPage = 15,
        CancellationToken cancellationToken = default)
    {
        var offset = int.TryParse(cursor, out var parsed) && parsed > 0 ? parsed : 0;

        var query = db.Contacts
            .AsNoTracking()
            .Include(c => c.Organization)
            .AsQueryable();

        if (organizationId is { } id)
        {
            query = query.Where(c => c.OrganizationId == id);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(c =>
                EF.Functions.Like(c.FirstName, $"%{term}%") ||
                EF.Functions.Like(c.LastName, $"%{term}%") ||
                (c.Email != null && EF.Functions.Like(c.Email, $"%{term}%")));
        }

        if (favorite)
        {
            query = query.Where(c => c.IsFavorite);
        }

        var rows = await query
            .OrderBy(c => c.FirstName)
            .ThenBy(c => c.LastName)
            .ThenBy(c => c.Id)
            .Skip(offset)
            .Take(perPage + 1)
            .ToListAsync(cancellationToken);

        var hasMore = rows.Count > perPage;
        if (hasMore)
        {
            rows.RemoveAt(rows.Count - 1);
        }

        var next = hasMore ? (offset + perPage).ToString() : null;
        var previous = offset > 0 ? Math.Max(0, offset - perPage).ToString() : null;

        return new CursorPage<ContactDto>(
            new CursorData<ContactDto> { Data = rows.Select(c => c.ToDto()).ToList() },
            ScrollMetadata.FromCursor(offset == 0 ? null : offset.ToString(), previous, next));
    }

    public async Task<Paginated<OrganizationDto>> OrganizationsAsync(
        string? search,
        int page,
        CancellationToken cancellationToken = default)
    {
        page = Math.Max(1, page);
        const int perPage = 20;

        var query = db.Organizations.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(o => EF.Functions.Like(o.Name, $"%{term}%"));
        }

        var total = await query.CountAsync(cancellationToken);
        var lastPage = Math.Max(1, (int)Math.Ceiling(total / (double)perPage));
        page = Math.Min(page, lastPage);

        var rows = await query
            .OrderBy(o => o.Name)
            .ThenBy(o => o.Id)
            .Skip((page - 1) * perPage)
            .Take(perPage)
            .Select(o => new OrganizationDto
            {
                Id = o.Id,
                Name = o.Name,
                ContactsCount = o.Contacts.Count,
                CreatedAt = o.CreatedAt,
            })
            .ToListAsync(cancellationToken);

        return new Paginated<OrganizationDto>
        {
            Data = rows,
            Meta = new PaginationMeta
            {
                CurrentPage = page,
                LastPage = lastPage,
                Links = BuildLinks(page, lastPage, search),
            },
        };
    }

    static List<PaginationLink> BuildLinks(int page, int lastPage, string? search)
    {
        string Url(int target)
        {
            var query = new List<string> { $"page={target}" };
            if (!string.IsNullOrWhiteSpace(search))
            {
                query.Add("search=" + Uri.EscapeDataString(search.Trim()));
            }

            return "/organizations?" + string.Join("&", query);
        }

        var links = new List<PaginationLink>
        {
            new()
            {
                Url = page > 1 ? Url(page - 1) : null,
                Label = "&laquo; Previous",
                Active = false,
            },
        };

        for (var i = 1; i <= lastPage; i++)
        {
            links.Add(new PaginationLink { Url = Url(i), Label = i.ToString(), Active = i == page });
        }

        links.Add(new PaginationLink
        {
            Url = page < lastPage ? Url(page + 1) : null,
            Label = "Next &raquo;",
            Active = false,
        });

        return links;
    }

    public async Task<ContactDto?> ContactAsync(int id, CancellationToken cancellationToken = default) =>
        (await db.Contacts.AsNoTracking().Include(c => c.Organization)
            .FirstOrDefaultAsync(c => c.Id == id, cancellationToken))?.ToDto();

    public async Task<List<NoteDto>> NotesAsync(int contactId, CancellationToken cancellationToken = default) =>
        (await db.Notes.AsNoTracking()
            .Include(n => n.User)
            .Where(n => n.ContactId == contactId)
            .OrderByDescending(n => n.CreatedAt)
            .ThenByDescending(n => n.Id)
            .ToListAsync(cancellationToken))
        .Select(n => n.ToDto())
        .ToList();

    public async Task<List<NoteDto>> RecentNotesAsync(int limit = 10, CancellationToken cancellationToken = default) =>
        (await db.Notes.AsNoTracking()
            .Include(n => n.Contact)
            .Include(n => n.User)
            .OrderByDescending(n => n.CreatedAt)
            .ThenByDescending(n => n.Id)
            .Take(limit)
            .ToListAsync(cancellationToken))
        .Select(n => n.ToDto())
        .ToList();

    public async Task<List<OrganizationDto>> OrganizationOptionsAsync(CancellationToken cancellationToken = default) =>
        await db.Organizations.AsNoTracking()
            .OrderBy(o => o.Name)
            .ThenBy(o => o.Id)
            .Select(o => new OrganizationDto { Id = o.Id, Name = o.Name, CreatedAt = o.CreatedAt })
            .ToListAsync(cancellationToken);

    public async Task<OrganizationDto?> OrganizationAsync(int id, CancellationToken cancellationToken = default) =>
        await db.Organizations.AsNoTracking()
            .Where(o => o.Id == id)
            .Select(o => new OrganizationDto
            {
                Id = o.Id,
                Name = o.Name,
                ContactsCount = o.Contacts.Count,
                CreatedAt = o.CreatedAt,
            })
            .FirstOrDefaultAsync(cancellationToken);
}

public sealed record CursorPage<T>(CursorData<T> Value, ScrollMetadata Metadata);
