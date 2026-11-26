using System.Numerics;
using System.Text.Json;
using KitchenSink.Data;
using KitchenSink.Features.Crm;
using KitchenSink.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace KitchenSink.Features.Showcase;

public sealed class FlakyReportException(string message) : Exception(message);

public static class DataLoadingFeature
{
    static string Now() => DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
    static string Time() => DateTime.Now.ToString("HH:mm:ss");

    public static IEndpointRouteBuilder MapDataLoading(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/features/data-loading").RequireAuthorization();

        group.MapGet("/deferred-props", DeferredProps);
        group.MapGet("/partial-reloads", PartialReloads);
        group.MapGet("/infinite-scroll", InfiniteScroll);
        group.MapGet("/when-visible", WhenVisible);
        group.MapGet("/polling", Polling);
        group.MapGet("/prop-merging", PropMerging);
        group.MapGet("/once-props/{page:int?}", OnceProps);
        group.MapGet("/optional-props", OptionalProps);
        group.MapGet("/big-integers", BigIntegers);
        group.MapPost("/big-integers", SubmitBigInteger);
        endpoints.MapLiveProps();

        return endpoints;
    }

    static IResult DeferredProps(HttpContext context, AppDb db)
    {
        return Inertia.Net.Inertia.Render("Features/DataLoading/DeferredProps", new InertiaProps
        {
            ["quickStat"] = "Loaded instantly",
            ["slowStats"] = Inertia.Net.Inertia.Defer(async ct =>
            {
                await Task.Delay(800, ct);
                return new
                {
                    totalContacts = await db.Contacts.CountAsync(ct),
                    totalFavorites = await db.Contacts.CountAsync(c => c.IsFavorite, ct),
                };
            }),
            ["heavyData"] = Inertia.Net.Inertia.Defer(async ct =>
            {
                await Task.Delay(1500, ct);
                return await db.Contacts.AsNoTracking()
                    .OrderByDescending(c => c.CreatedAt)
                    .ThenByDescending(c => c.Id)
                    .Take(5)
                    .Select(c => new { id = c.Id, name = c.FirstName + " " + c.LastName })
                    .ToListAsync(ct);
            }, "heavy"),
            ["flakyReport"] = Inertia.Net.Inertia.Defer(async ct =>
            {
                await Task.Delay(600, ct);
                if (!context.Request.Headers.ContainsKey("X-Force-Success"))
                {
                    throw new FlakyReportException("Upstream report service is unavailable.");
                }

                return new { value = Random.Shared.Next(1000, 10000) };
            }).Rescue(),
        });
    }

    static IResult PartialReloads(AppDb db)
    {
        return Inertia.Net.Inertia.Render("Features/DataLoading/PartialReloads", new InertiaProps
        {
            ["users"] = Inertia.Net.Inertia.Prop(() => new[]
            {
                new { id = 1, name = "Alice", role = "Admin" },
                new { id = 2, name = "Bob", role = "Editor" },
                new { id = 3, name = "Charlie", role = "Viewer" },
            }),
            ["stats"] = Inertia.Net.Inertia.Prop(async ct => new
            {
                total = await db.Contacts.CountAsync(ct),
                favorites = await db.Contacts.CountAsync(c => c.IsFavorite, ct),
            }),
            ["timestamp"] = Inertia.Net.Inertia.Prop(Now),
            ["randomNumber"] = Inertia.Net.Inertia.Prop(() => Random.Shared.Next(1, 1001)),
        });
    }

    static async Task<IResult> InfiniteScroll(HttpContext context, CrmService service, CancellationToken cancellationToken)
    {
        var favorites = context.Request.Query["favorites"].ToString() is "1" or "true";
        var cursor = context.Request.Query["cursor"].ToString();
        var page = await service.ContactsAsync(null, favorites, cursor, perPage: 10, cancellationToken: cancellationToken);

        return Inertia.Net.Inertia.Render("Features/DataLoading/InfiniteScroll", new InertiaProps
        {
            ["contacts"] = Inertia.Net.Inertia.Scroll(page.Value, "data", page.Metadata),
        });
    }

    static IResult WhenVisible(AppDb db)
    {
        return Inertia.Net.Inertia.Render("Features/DataLoading/WhenVisible", new InertiaProps
        {
            ["section1"] = Inertia.Net.Inertia.Optional(async ct =>
            {
                await Task.Delay(500, ct);
                return await db.Contacts.AsNoTracking()
                    .OrderBy(c => c.Id)
                    .Take(3)
                    .Select(c => new { id = c.Id, name = c.FirstName + " " + c.LastName })
                    .ToListAsync(ct);
            }),
            ["section2"] = Inertia.Net.Inertia.Optional(async ct =>
            {
                await Task.Delay(800, ct);
                return new
                {
                    totalContacts = await db.Contacts.CountAsync(ct),
                    generated = Now(),
                };
            }),
            ["section3"] = Inertia.Net.Inertia.Optional(async ct =>
            {
                await Task.Delay(600, ct);
                return await db.Contacts.AsNoTracking()
                    .Where(c => c.IsFavorite)
                    .Take(5)
                    .Select(c => new { id = c.Id, name = c.FirstName + " " + c.LastName })
                    .ToListAsync(ct);
            }),
        });
    }

    static IResult Polling(AppDb db)
    {
        return Inertia.Net.Inertia.Render("Features/DataLoading/Polling", new InertiaProps
        {
            ["currentTime"] = Now(),
            ["randomNumber"] = Inertia.Net.Inertia.Prop(async ct =>
            {
                await Task.Delay(Random.Shared.Next(500, 3001), ct);
                return Random.Shared.Next(1, 1001);
            }),
            ["contactCount"] = Inertia.Net.Inertia.Prop(async ct => await db.Contacts.CountAsync(ct)),
        });
    }

    static IResult PropMerging(HttpContext context)
    {
        var resetProps = context.Request.Headers["X-Inertia-Reset"].ToString()
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var isPartialReload = context.Request.Headers.ContainsKey("X-Inertia-Partial-Data");
        var count = 1;

        if (resetProps.Contains("contacts", StringComparer.Ordinal))
        {
            count = 1;
        }
        else if (isPartialReload)
        {
            count = Math.Min((context.Session.GetInt32("prop_merging_count") ?? 1) + 1, 5);
        }

        context.Session.SetInt32("prop_merging_count", count);

        var pool = new[]
        {
            new { id = 1, name = "Alice" },
            new { id = 2, name = "Bob" },
            new { id = 3, name = "Charlie" },
            new { id = 4, name = "Diana" },
            new { id = 5, name = "Eve" },
        };

        return Inertia.Net.Inertia.Render("Features/DataLoading/PropMerging", new InertiaProps
        {
            ["notifications"] = Inertia.Net.Inertia.Merge(new[]
            {
                new
                {
                    id = Random.Shared.Next(1000, 10000),
                    message = $"Notification at {Time()}",
                    type = new[] { "info", "success", "warning", "error", "alert", "update", "reminder", "system", "promo", "social" }[Random.Shared.Next(10)],
                },
            }),
            ["activities"] = Inertia.Net.Inertia.Merge(new[]
            {
                new
                {
                    id = Random.Shared.Next(1000, 10000),
                    action = new[] { "created", "updated", "deleted", "restored", "archived", "published" }[Random.Shared.Next(6)],
                    subject = new[] { "Contact", "Organization", "Note", "Invoice", "Report", "Task" }[Random.Shared.Next(6)],
                    time = Time(),
                },
            }).Prepend(),
            ["contacts"] = Inertia.Net.Inertia.Merge(pool.Take(count).Select(c => new
            {
                c.id,
                c.name,
                updated = Time(),
            })).MatchOn("id"),
            ["timestamp"] = Now(),
        });
    }

    static IResult OnceProps(int? page)
    {
        var number = page is 1 or 2 ? page.Value : 1;

        return Inertia.Net.Inertia.Render("Features/DataLoading/OnceProps", new InertiaProps
        {
            ["page"] = number,
            ["staticData"] = Inertia.Net.Inertia.Once(() => new
            {
                generatedAt = Now(),
                randomId = Random.Shared.Next(1000, 10000),
            }),
            ["freshData"] = Inertia.Net.Inertia.Once(() => new
            {
                generatedAt = Now(),
                value = Random.Shared.Next(1, 1001),
            }).Fresh(),
            ["expiringData"] = Inertia.Net.Inertia.Once(() => new
            {
                generatedAt = Now(),
                value = Random.Shared.Next(1, 1001),
            }).Until(TimeSpan.FromSeconds(5)),
            ["aliasedData"] = Inertia.Net.Inertia.Once(() => new
            {
                generatedAt = Now(),
                value = Random.Shared.Next(1, 1001),
            }).As("shared-once-key"),
            ["dynamicData"] = new
            {
                timestamp = Now(),
                randomNumber = Random.Shared.Next(1, 1001),
            },
        });
    }

    static IResult OptionalProps(AppDb db)
    {
        return Inertia.Net.Inertia.Render("Features/DataLoading/OptionalProps", new InertiaProps
        {
            ["regularData"] = new
            {
                timestamp = Now(),
                message = "This prop is always included in the response.",
            },
            ["optionalData"] = Inertia.Net.Inertia.Optional(async ct =>
            {
                await Task.Delay(500, ct);
                return new
                {
                    generatedAt = Now(),
                    contacts = await db.Contacts.AsNoTracking()
                        .OrderBy(c => c.Id)
                        .Take(3)
                        .Select(c => new { id = c.Id, name = c.FirstName + " " + c.LastName })
                        .ToListAsync(ct),
                };
            }),
            ["deferredData"] = Inertia.Net.Inertia.Defer(async ct =>
            {
                await Task.Delay(600, ct);
                return new
                {
                    generatedAt = Now(),
                    totalContacts = await db.Contacts.CountAsync(ct),
                };
            }),
        });
    }

    static IResult BigIntegers(HttpRequest request)
    {
        var preserve = request.Query["preserve"].ToString() is not ("0" or "false");

        // A JsonElement goes through the page writer's big-integer pass and becomes {"$bigint":"..."};
        // a double stays a JSON number, so the browser rounds it like the disabled demo shows.
        object orderId = preserve
            ? JsonDocument.Parse("900719925474099988").RootElement.Clone()
            : 900719925474099988d;

        return Inertia.Net.Inertia.Render("Features/DataLoading/BigIntegers", new InertiaProps
        {
            ["preserve"] = preserve,
            ["orderId"] = orderId,
            ["expectedOrderId"] = "900719925474099988",
            ["customerId"] = 42,
        });
    }

    static async Task<IResult> SubmitBigInteger(HttpContext context, CancellationToken cancellationToken)
    {
        var input = await RequestInput.ReadAsync(context.Request, cancellationToken);
        var id = input.String("id");

        if (string.IsNullOrWhiteSpace(id) || !BigInteger.TryParse(id, out _))
        {
            return InertiaForms.BackWithErrors(context, new Dictionary<string, string[]>
            {
                ["id"] = ["The id field is required."],
            });
        }

        return Inertia.Net.Inertia.Back().WithFlash("receivedOrderId", id);
    }
}
