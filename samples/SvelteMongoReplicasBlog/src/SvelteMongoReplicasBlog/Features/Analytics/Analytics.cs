using MongoDB.Bson;
using MongoDB.Driver;
using SvelteMongoReplicasBlog.Data;
using SvelteMongoReplicasBlog.Infrastructure;
using static SvelteMongoReplicasBlog.Features.AnalyticsHelpers;

namespace SvelteMongoReplicasBlog.Features;

public static class Analytics
{
    // One skipped aggregation per prop: page visits reload these within a minute far more often than new events land.
    // The key carries the day window, so the new window after midnight is a different entry.
    static readonly TimeSpan AnalyticsTtl = TimeSpan.FromSeconds(60);

    public static void MapAnalytics(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/analytics").RequireAuthorization();
        group.MapGet("/", Get);
        group.MapGet("/posts/{id:guid}", FeedAsync);
    }

    internal static IResult Get(int? days, HttpContext ctx, AppDb db)
    {
        var n = Days(days);
        var since = Today.AddDays(1 - n);
        var uid = ctx.User.UserId();
        return Render(
            "Analytics/Index",
            new InertiaProps
            {
                ["days"] = n,
                // Cached props (Inertia.Net 1.4): the value is kept as JSON in the app's IMemoryCache, so the loader runs
                // at most once a minute per user, range and day window.
                ["kpis"] = Defer(
                    async ct =>
                    {
                        var ids = await db
                            .Posts.Find(p => p.AuthorId == uid)
                            .Project(p => p.Id)
                            .ToListAsync(ct);
                        var agg =
                            ids.Count == 0
                                ? null
                                : (await AnalyticsService.Grouped(db, ids, since, "$literal", ct)).FirstOrDefault();
                        return new Kpis(
                            agg is null ? 0 : AnalyticsService.Value(agg, "Views"),
                            agg is null ? 0 : AnalyticsService.Value(agg, "Unique"),
                            agg is null ? 0 : AnalyticsService.Value(agg, "Likes"),
                            agg is null ? 0 : AnalyticsService.Value(agg, "Comments"),
                            ids.Count
                        );
                    },
                    "kpis"
                ).Cache($"analytics:{uid}:{n}:{since:yyyyMMdd}:kpis", AnalyticsTtl),
                ["series"] = Defer(
                    async ct =>
                    {
                        var ids = await db
                            .Posts.Find(p => p.AuthorId == uid)
                            .Project(p => p.Id)
                            .ToListAsync(ct);
                        return await AnalyticsService.Series(db, ids, since, n, ct);
                    },
                    "charts"
                ).Cache($"analytics:{uid}:{n}:{since:yyyyMMdd}:series", AnalyticsTtl),
                ["topPosts"] = Defer(
                    async ct =>
                    {
                        var ids = await db
                            .Posts.Find(p => p.AuthorId == uid)
                            .Project(p => p.Id)
                            .ToListAsync(ct);
                        if (ids.Count == 0)
                            return new List<TopPost>();
                        var agg = await AnalyticsService.Grouped(db, ids, since, "$PostId", ct);
                        var top = agg.OrderByDescending(x => AnalyticsService.Value(x, "Views"))
                            .ThenByDescending(x => AnalyticsService.Value(x, "Likes"))
                            .Take(10)
                            .ToList();
                        var topIds = top.Select(x => x["_id"].AsGuid).ToArray();
                        var posts = await db.Posts.Find(p => topIds.Contains(p.Id)).ToListAsync(ct);
                        var byId = posts.ToDictionary(p => p.Id);
                        return top.Where(x => byId.ContainsKey(x["_id"].AsGuid))
                            .Select(x =>
                            {
                                var p = byId[x["_id"].AsGuid];
                                return new TopPost(
                                    p.Id,
                                    p.Slug,
                                    p.Title,
                                    AnalyticsService.Value(x, "Views"),
                                    AnalyticsService.Value(x, "Likes"),
                                    AnalyticsService.Value(x, "Comments")
                                );
                            })
                            .ToList();
                    },
                    "charts"
                ).Cache($"analytics:{uid}:{n}:{since:yyyyMMdd}:topPosts", AnalyticsTtl),
            }
        );
    }

    internal static async Task<IResult> FeedAsync(
        Guid id,
        int? days,
        HttpContext ctx,
        AppDb db,
        CancellationToken ct
    )
    {
        var post = await db.Posts.Find(p => p.Id == id).FirstOrDefaultAsync(ct);
        if (post is null)
            return Render("Error", new InertiaProps { ["status"] = 404 }).WithStatusCode(404);
        var uid = ctx.User.UserId();
        if (post.AuthorId != uid)
            return HttpExtensions.Forbidden();
        var n = Days(days);
        var since = Today.AddDays(1 - n);
        var fromAt = AnalyticsService.Utc(since);
        return Render(
            "Analytics/Post",
            new InertiaProps
            {
                ["days"] = n,
                ["post"] = new
                {
                    post.Id,
                    post.Title,
                    post.Slug,
                },
                ["kpis"] = Defer(
                    async c =>
                    {
                        var agg = (await AnalyticsService.Grouped(db, [id], since, "$literal", c)).FirstOrDefault();
                        return new Kpis(
                            agg is null ? 0 : AnalyticsService.Value(agg, "Views"),
                            agg is null ? 0 : AnalyticsService.Value(agg, "Unique"),
                            agg is null ? 0 : AnalyticsService.Value(agg, "Likes"),
                            agg is null ? 0 : AnalyticsService.Value(agg, "Comments"),
                            1
                        );
                    },
                    "kpis"
                ).Cache($"analytics-post:{uid}:{id}:{n}:{since:yyyyMMdd}:kpis", AnalyticsTtl),
                ["series"] = Defer(c => AnalyticsService.Series(db, [id], since, n, c), "charts")
                    .Cache($"analytics-post:{uid}:{id}:{n}:{since:yyyyMMdd}:series", AnalyticsTtl),
                ["referrers"] = Defer(
                    async c =>
                    {
                        var rows = await db
                            .PostViews.Aggregate<BsonDocument>(
                                PipelineDefinition<PostView, BsonDocument>.Create(
                                    new BsonDocument[]
                                    {
                                        new BsonDocument(
                                            "$match",
                                            new BsonDocument
                                            {
                                                ["PostId"] = Bin(id),
                                                ["At"] = new BsonDocument("$gte", fromAt),
                                            }
                                        ),
                                        new BsonDocument(
                                            "$group",
                                            new BsonDocument
                                            {
                                                ["_id"] = "$Referrer",
                                                ["Views"] = new BsonDocument("$sum", 1),
                                            }
                                        ),
                                        new BsonDocument("$sort", new BsonDocument("Views", -1)),
                                        new BsonDocument("$limit", 10),
                                    }
                                )
                            )
                            .ToListAsync(c);
                        return rows.Select(x => new Referrer(
                                x["_id"].IsBsonNull ? "Direct" : x["_id"].AsString,
                                AnalyticsService.Value(x, "Views")
                            ))
                            .ToList();
                    },
                    "charts"
                ).Cache($"analytics-post:{uid}:{id}:{n}:{since:yyyyMMdd}:referrers", AnalyticsTtl),
            }
        );
    }
}
