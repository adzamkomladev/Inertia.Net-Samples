using System.Text.Json;
using KitchenSink.Data;
using Microsoft.EntityFrameworkCore;

namespace KitchenSink.Features.Showcase;

public static class NavigationFeature
{
    static string Now() => DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
    static string Clock() => DateTime.Now.ToString("HH:mm:ss.fff");

    public static IEndpointRouteBuilder MapNavigation(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/features/navigation").RequireAuthorization();

        group.MapMethods("/links", ["GET", "QUERY"], () =>
            Inertia.Net.Inertia.Render("Features/Navigation/Links", new InertiaProps
            {
                ["timestamp"] = Now(),
            }));
        group.MapMethods("/links", ["POST", "PUT", "PATCH", "DELETE"], (HttpContext context) =>
            Inertia.Net.Inertia.Back().WithFlash(
                "message",
                $"{context.Request.Method.ToUpperInvariant()} request received at {DateTime.Now:HH:mm:ss}"));

        group.MapGet("/preserve-state", () =>
            Inertia.Net.Inertia.Render("Features/Navigation/PreserveState", new InertiaProps
            {
                ["serverCounter"] = Random.Shared.Next(1, 1001),
                ["timestamp"] = Now(),
            }));

        group.MapGet("/preserve-scroll", () =>
            Inertia.Net.Inertia.Render("Features/Navigation/PreserveScroll", new InertiaProps
            {
                ["timestamp"] = Now(),
            }));

        group.MapGet("/view-transitions", () => Inertia.Net.Inertia.Render("Features/Navigation/ViewTransitions"));

        group.MapGet("/history-management", (HttpRequest request) =>
            Inertia.Net.Inertia.Render("Features/Navigation/HistoryManagement", new InertiaProps
            {
                ["visit"] = int.TryParse(request.Query["visit"], out var visit) ? visit : 0,
                ["timestamp"] = Now(),
            }));
        group.MapPost("/history-management", () => Results.Redirect("/features/navigation/history-management"));

        group.MapGet("/async-requests", async (HttpContext context, CancellationToken cancellationToken) =>
        {
            if (context.Request.Headers.ContainsKey("X-Inertia"))
            {
                await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
            }

            return Inertia.Net.Inertia.Render("Features/Navigation/AsyncRequests", new InertiaProps
            {
                ["timestamp"] = Now(),
            });
        });

        group.MapGet("/async-slow", async (HttpRequest request, CancellationToken cancellationToken) =>
        {
            var delay = int.TryParse(request.Query["delay"], out var seconds) ? seconds : 2;
            await Task.Delay(TimeSpan.FromSeconds(Math.Clamp(delay, 0, 5)), cancellationToken);

            return Inertia.Net.Inertia.Render("Features/Navigation/AsyncRequests", new InertiaProps
            {
                ["timestamp"] = Now(),
            });
        });

        group.MapGet("/instant-visits", () =>
            Inertia.Net.Inertia.Render("Features/Navigation/InstantVisits", new InertiaProps
            {
                ["sourceTimestamp"] = Now(),
                ["message"] = "This is the source page.",
            }));

        group.MapGet("/instant-visit-target", async (HttpRequest request, CancellationToken cancellationToken) =>
        {
            var delay = int.TryParse(request.Query["delay"], out var seconds) ? seconds : 2;
            await Task.Delay(TimeSpan.FromSeconds(Math.Clamp(delay, 0, 5)), cancellationToken);

            return Inertia.Net.Inertia.Render("Features/Navigation/InstantVisitTarget", new InertiaProps
            {
                ["greeting"] = "Hello from the server!",
                ["serverTimestamp"] = Now(),
                ["items"] = new[]
                {
                    new { id = 1, name = "Server Item A" },
                    new { id = 2, name = "Server Item B" },
                    new { id = 3, name = "Server Item C" },
                },
            });
        });

        group.MapGet("/manual-visits", () =>
            Inertia.Net.Inertia.Render("Features/Navigation/ManualVisits", new InertiaProps
            {
                ["timestamp"] = Now(),
                ["counter"] = Random.Shared.Next(1, 1001),
            }));

        group.MapMethods("/query-visits", ["GET", "QUERY"], QueryVisits);

        group.MapGet("/redirects", () =>
            Inertia.Net.Inertia.Render("Features/Navigation/Redirects", new InertiaProps
            {
                ["timestamp"] = Now(),
            }));
        group.MapPost("/redirects/back", () =>
            Inertia.Net.Inertia.Back().WithFlash("message", "Redirected back via redirect()->back()"));
        group.MapPost("/redirects/to-route", (HttpContext context) =>
        {
            context.Inertia().Flash("message", "Redirected via to_route()");
            return Results.Redirect("/features/navigation/redirects");
        });
        group.MapPost("/redirects/external", () => Inertia.Net.Inertia.Location("https://cloud.laravel.com"));

        group.MapGet("/scroll-management", () =>
            Inertia.Net.Inertia.Render("Features/Navigation/ScrollManagement", new InertiaProps
            {
                ["timestamp"] = Now(),
                ["items"] = Enumerable.Range(1, 50).Select(i => new
                {
                    id = i,
                    title = $"Item #{i}",
                    description = $"This is item number {i}. Scroll down to test scroll management.",
                }).ToList(),
            }));

        group.MapGet("/url-fragments", () =>
            Inertia.Net.Inertia.Render("Features/Navigation/UrlFragments", new InertiaProps
            {
                ["timestamp"] = Now(),
            }));
        group.MapMethods("/url-fragments/redirect-hash", ["GET", "POST"], () =>
            Results.Redirect("/features/navigation/url-fragments#server-section"));
        group.MapGet("/url-fragments/preserve-target", () =>
            Inertia.Net.Inertia.Render("Features/Navigation/UrlFragments", new InertiaProps
            {
                ["timestamp"] = Now(),
                ["redirectedFrom"] = "preserveFragment redirect",
            }));
        group.MapGet("/url-fragments/preserve-redirect", (HttpContext context) =>
        {
            context.Inertia().PreserveFragment();
            return Results.Redirect("/features/navigation/url-fragments/preserve-target");
        });

        return endpoints;
    }

    static async Task<IResult> QueryVisits(HttpContext context, AppDb db, IConfiguration config, CancellationToken cancellationToken)
    {
        if (!config.GetValue("Demo:QueryMethod", true))
        {
            return Results.NotFound();
        }

        string search;
        List<int> organizations;
        bool favorites;
        string? body = null;
        var isQuery = string.Equals(context.Request.Method, "QUERY", StringComparison.OrdinalIgnoreCase);

        if (isQuery || context.Request.HasJsonContentType())
        {
            using var reader = new StreamReader(context.Request.Body);
            body = await reader.ReadToEndAsync(cancellationToken);
            using var document = JsonDocument.Parse(string.IsNullOrWhiteSpace(body) ? "{}" : body);
            var root = document.RootElement;
            search = root.TryGetProperty("search", out var searchValue) ? searchValue.GetString() ?? "" : "";
            organizations = root.TryGetProperty("organizations", out var orgs) && orgs.ValueKind == JsonValueKind.Array
                ? orgs.EnumerateArray().Select(item => item.ValueKind == JsonValueKind.Number ? item.GetInt32() : int.TryParse(item.GetString(), out var id) ? id : 0).Where(id => id > 0).ToList()
                : [];
            favorites = root.TryGetProperty("favorites", out var favoriteValue)
                && (favoriteValue.ValueKind == JsonValueKind.True
                    || (favoriteValue.ValueKind == JsonValueKind.Number && favoriteValue.GetInt32() == 1)
                    || (favoriteValue.ValueKind == JsonValueKind.String && favoriteValue.GetString() is "1" or "true"));
        }
        else
        {
            search = context.Request.Query["search"].ToString();
            organizations = context.Request.Query["organizations"]
                .SelectMany(value => (value ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries))
                .Select(value => int.TryParse(value, out var id) ? id : 0)
                .Where(id => id > 0)
                .ToList();
            favorites = context.Request.Query["favorites"].ToString() is "1" or "true";
        }

        var query = db.Contacts.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(c =>
                EF.Functions.Like(c.FirstName, $"%{term}%") ||
                EF.Functions.Like(c.LastName, $"%{term}%") ||
                (c.Email != null && EF.Functions.Like(c.Email, $"%{term}%")));
        }
        if (organizations.Count > 0)
        {
            query = query.Where(c => c.OrganizationId != null && organizations.Contains(c.OrganizationId.Value));
        }
        if (favorites)
        {
            query = query.Where(c => c.IsFavorite);
        }

        var rows = await query
            .Include(c => c.Organization)
            .OrderBy(c => c.LastName)
            .ThenBy(c => c.Id)
            .Take(8)
            .ToListAsync(cancellationToken);

        var organizationRows = await db.Organizations.AsNoTracking()
            .OrderBy(o => o.Name)
            .ThenBy(o => o.Id)
            .Select(o => new { id = o.Id, name = o.Name })
            .ToListAsync(cancellationToken);

        return Inertia.Net.Inertia.Render("Features/Navigation/QueryVisits", new InertiaProps
        {
            ["filters"] = new
            {
                search,
                organizations,
                favorites,
            },
            ["organizations"] = organizationRows,
            ["contacts"] = Inertia.Net.Inertia.Prop(() => rows.Select(c => new
            {
                id = c.Id,
                name = c.Name,
                organization = c.Organization?.Name,
                isFavorite = c.IsFavorite,
            }).ToList()),
            ["matches"] = Inertia.Net.Inertia.Defer(async ct => await query.CountAsync(ct)),
            ["lastRequest"] = Inertia.Net.Inertia.Prop(() => new
            {
                method = context.Request.Method,
                body = string.IsNullOrWhiteSpace(body) ? null : body,
                at = Clock(),
            }),
        });
    }
}
