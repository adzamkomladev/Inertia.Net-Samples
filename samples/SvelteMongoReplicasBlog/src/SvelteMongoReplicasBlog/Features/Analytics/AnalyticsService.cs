using MongoDB.Bson;
using MongoDB.Driver;
using SvelteMongoReplicasBlog.Data;
using static SvelteMongoReplicasBlog.Features.AnalyticsHelpers;

namespace SvelteMongoReplicasBlog.Features;

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
    internal static async Task<List<Guid>> AuthorPostIdsAsync(AppDb db, Guid? uid, CancellationToken ct) =>
        uid is null
            ? []
            : await db.Posts.Find(p => p.AuthorId == uid).Project(p => p.Id).ToListAsync(ct);

    internal static async Task<Kpis> KpisDataAsync(AppDb db, Guid uid, DateOnly since, CancellationToken ct)
    {
        var ids = await AuthorPostIdsAsync(db, uid, ct);
        var agg =
            ids.Count == 0 ? null : (await Grouped(db, ids, since, "$literal", ct)).FirstOrDefault();
        return new Kpis(
            agg is null ? 0 : Value(agg, "Views"),
            agg is null ? 0 : Value(agg, "Unique"),
            agg is null ? 0 : Value(agg, "Likes"),
            agg is null ? 0 : Value(agg, "Comments"),
            ids.Count
        );
    }

    internal static async Task<List<SeriesPoint>> SeriesDataAsync(
        AppDb db,
        Guid uid,
        DateOnly since,
        int days,
        CancellationToken ct
    ) => await Series(db, await AuthorPostIdsAsync(db, uid, ct), since, days, ct);

    internal static async Task<List<TopPost>> TopPostsDataAsync(
        AppDb db,
        Guid uid,
        DateOnly since,
        CancellationToken ct
    )
    {
        var ids = await AuthorPostIdsAsync(db, uid, ct);
        if (ids.Count == 0)
            return [];
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
    }

    internal static async Task<PostHeader?> PostHeaderAsync(
        AppDb db,
        Guid id,
        CancellationToken ct
    )
    {
        var post = await db.Posts.Find(p => p.Id == id).FirstOrDefaultAsync(ct);
        return post is null
            ? null
            : new PostHeader(post.Id, post.Title, post.Slug, post.AuthorId);
    }

    internal static async Task<Kpis> PostKpisDataAsync(AppDb db, Guid id, DateOnly since, CancellationToken ct)
    {
        var agg = (await Grouped(db, [id], since, "$literal", ct)).FirstOrDefault();
        return new Kpis(
            agg is null ? 0 : Value(agg, "Views"),
            agg is null ? 0 : Value(agg, "Unique"),
            agg is null ? 0 : Value(agg, "Likes"),
            agg is null ? 0 : Value(agg, "Comments"),
            1
        );
    }

    internal static async Task<List<Referrer>> ReferrersDataAsync(
        AppDb db,
        Guid id,
        DateTime fromAt,
        CancellationToken ct
    )
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
            .ToListAsync(ct);
        return rows.Select(x => new Referrer(
                x["_id"].IsBsonNull ? "Direct" : x["_id"].AsString,
                Value(x, "Views")
            ))
            .ToList();
    }

}
