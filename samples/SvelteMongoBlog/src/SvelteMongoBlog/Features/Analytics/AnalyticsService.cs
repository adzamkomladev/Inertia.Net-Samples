using MongoDB.Bson;
using MongoDB.Driver;
using SvelteMongoBlog.Data;
using SvelteMongoBlog.Infrastructure;
using static SvelteMongoBlog.Features.AnalyticsHelpers;

namespace SvelteMongoBlog.Features;

public static partial class AnalyticsService
{
    internal static async Task<List<BsonDocument>> RawRows(
        AppDb db,
        Guid[] ids,
        DateOnly since,
        CancellationToken ct
    )
    {
        if (ids.Length == 0)
            return [];
        var start = Utc(since);
        var filterViews = new BsonDocument(
            "$match",
            new BsonDocument
            {
                ["PostId"] = new BsonDocument("$in", new BsonArray(ids.Select(Bin))),
                ["At"] = new BsonDocument("$gte", start),
            }
        );
        var filterLikes = new BsonDocument(
            "$match",
            new BsonDocument
            {
                ["PostId"] = new BsonDocument("$in", new BsonArray(ids.Select(Bin))),
                ["CreatedAt"] = new BsonDocument("$gte", start),
            }
        );
        var daily = new BsonDocument(
            "$dateToString",
            new BsonDocument { ["format"] = "%Y-%m-%d", ["date"] = "$At" }
        );
        var dailyCreated = new BsonDocument(
            "$dateToString",
            new BsonDocument { ["format"] = "%Y-%m-%d", ["date"] = "$CreatedAt" }
        );
        var views = await db
            .PostViews.Aggregate<BsonDocument>(
                PipelineDefinition<PostView, BsonDocument>.Create(
                    new BsonDocument[]
                    {
                        filterViews,
                        new(
                            "$group",
                            new BsonDocument
                            {
                                ["_id"] = new BsonDocument
                                {
                                    ["PostId"] = "$PostId",
                                    ["Date"] = daily,
                                    ["ViewerHash"] = "$ViewerHash",
                                },
                                ["Views"] = new BsonDocument("$sum", 1),
                            }
                        ),
                        new(
                            "$group",
                            new BsonDocument
                            {
                                ["_id"] = new BsonDocument
                                {
                                    ["PostId"] = "$_id.PostId",
                                    ["Date"] = "$_id.Date",
                                },
                                ["Views"] = new BsonDocument("$sum", "$Views"),
                                ["Unique"] = new BsonDocument("$sum", 1),
                            }
                        ),
                    }
                )
            )
            .ToListAsync(ct);
        var likes = await db
            .Likes.Aggregate<BsonDocument>(
                PipelineDefinition<Like, BsonDocument>.Create(
                    new BsonDocument[]
                    {
                        filterLikes,
                        new(
                            "$group",
                            new BsonDocument
                            {
                                ["_id"] = new BsonDocument
                                {
                                    ["PostId"] = "$PostId",
                                    ["Date"] = dailyCreated,
                                },
                                ["Likes"] = new BsonDocument("$sum", 1),
                            }
                        ),
                    }
                )
            )
            .ToListAsync(ct);
        var comments = await db
            .Comments.Aggregate<BsonDocument>(
                PipelineDefinition<Comment, BsonDocument>.Create(
                    new BsonDocument[]
                    {
                        filterLikes,
                        new(
                            "$group",
                            new BsonDocument
                            {
                                ["_id"] = new BsonDocument
                                {
                                    ["PostId"] = "$PostId",
                                    ["Date"] = dailyCreated,
                                },
                                ["Comments"] = new BsonDocument("$sum", 1),
                            }
                        ),
                    }
                )
            )
            .ToListAsync(ct);
        var rows = new Dictionary<(Guid Id, string Date), BsonDocument>();
        BsonDocument Get(BsonDocument row)
        {
            var key = row["_id"].AsBsonDocument;
            var id = key["PostId"].AsGuid;
            var date = key["Date"].AsString;
            if (!rows.TryGetValue((id, date), out var value))
                rows[(id, date)] = value = new BsonDocument
                {
                    ["PostId"] = Bin(id),
                    ["Date"] = date,
                    ["Views"] = 0,
                    ["Unique"] = 0,
                    ["Likes"] = 0,
                    ["Comments"] = 0,
                };
            return value;
        }

        foreach (var row in views)
        {
            var target = Get(row);
            target["Views"] = row["Views"];
            target["Unique"] = row["Unique"];
        }

        foreach (var row in likes)
            Get(row)["Likes"] = row["Likes"];
        foreach (var row in comments)
            Get(row)["Comments"] = row["Comments"];
        return rows.Values.ToList();
    }

    internal static async Task<List<BsonDocument>> Grouped(
        AppDb db,
        IEnumerable<Guid> ids,
        DateOnly since,
        string key,
        CancellationToken ct
    )
    {
        var rows = await RawRows(db, ids.ToArray(), since, ct);
        return rows.GroupBy(row => key == "$PostId" ? row["PostId"].AsGuid : Guid.Empty)
            .Select(g => new BsonDocument
            {
                ["_id"] = key == "$PostId" ? Bin(g.Key) : (BsonValue)1,
                ["Views"] = g.Sum(x => Value(x, "Views")),
                ["Unique"] = g.Sum(x => Value(x, "Unique")),
                ["Likes"] = g.Sum(x => Value(x, "Likes")),
                ["Comments"] = g.Sum(x => Value(x, "Comments")),
            })
            .ToList();
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
                                : (await Grouped(db, ids, since, "$literal", ct)).FirstOrDefault();
                        return new Kpis(
                            agg is null ? 0 : Value(agg, "Views"),
                            agg is null ? 0 : Value(agg, "Unique"),
                            agg is null ? 0 : Value(agg, "Likes"),
                            agg is null ? 0 : Value(agg, "Comments"),
                            ids.Count
                        );
                    },
                    "kpis"
                ),
                ["series"] = Defer(
                    async ct =>
                    {
                        var ids = await db
                            .Posts.Find(p => p.AuthorId == uid)
                            .Project(p => p.Id)
                            .ToListAsync(ct);
                        return await Series(db, ids, since, n, ct);
                    },
                    "charts"
                ),
                ["topPosts"] = Defer(
                    async ct =>
                    {
                        var ids = await db
                            .Posts.Find(p => p.AuthorId == uid)
                            .Project(p => p.Id)
                            .ToListAsync(ct);
                        if (ids.Count == 0)
                            return new List<TopPost>();
                        var agg = await Grouped(db, ids, since, "$PostId", ct);
                        var top = agg.OrderByDescending(x => Value(x, "Views"))
                            .ThenByDescending(x => Value(x, "Likes"))
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
                                    Value(x, "Views"),
                                    Value(x, "Likes"),
                                    Value(x, "Comments")
                                );
                            })
                            .ToList();
                    },
                    "charts"
                ),
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
        if (post.AuthorId != ctx.User.UserId())
            return HttpExtensions.Forbidden();
        var n = Days(days);
        var since = Today.AddDays(1 - n);
        var fromAt = Utc(since);
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
                        var agg = (await Grouped(db, [id], since, "$literal", c)).FirstOrDefault();
                        return new Kpis(
                            agg is null ? 0 : Value(agg, "Views"),
                            agg is null ? 0 : Value(agg, "Unique"),
                            agg is null ? 0 : Value(agg, "Likes"),
                            agg is null ? 0 : Value(agg, "Comments"),
                            1
                        );
                    },
                    "kpis"
                ),
                ["series"] = Defer(c => Series(db, [id], since, n, c), "charts"),
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
                                Value(x, "Views")
                            ))
                            .ToList();
                    },
                    "charts"
                ),
            }
        );
    }

    internal static async Task<List<SeriesPoint>> Series(
        AppDb db,
        IEnumerable<Guid> ids,
        DateOnly since,
        int days,
        CancellationToken ct
    )
    {
        var idArray = ids.ToArray();
        if (idArray.Length == 0)
            return Enumerable
                .Range(0, days)
                .Select(i => new SeriesPoint(since.AddDays(i).ToString("yyyy-MM-dd"), 0, 0, 0, 0))
                .ToList();
        var rows = await RawRows(db, idArray, since, ct);
        var byDate = rows.GroupBy(x => x["Date"].AsString)
            .ToDictionary(
                g => DateOnly.Parse(g.Key),
                g => new BsonDocument
                {
                    ["Views"] = g.Sum(x => Value(x, "Views")),
                    ["Unique"] = g.Sum(x => Value(x, "Unique")),
                    ["Likes"] = g.Sum(x => Value(x, "Likes")),
                    ["Comments"] = g.Sum(x => Value(x, "Comments")),
                }
            );
        return Enumerable
            .Range(0, days)
            .Select(i =>
            {
                var day = since.AddDays(i);
                return byDate.TryGetValue(day, out var row)
                    ? new SeriesPoint(
                        day.ToString("yyyy-MM-dd"),
                        Value(row, "Views"),
                        Value(row, "Unique"),
                        Value(row, "Likes"),
                        Value(row, "Comments")
                    )
                    : new SeriesPoint(day.ToString("yyyy-MM-dd"), 0, 0, 0, 0);
            })
            .ToList();
    }
}
