using Bogus;
using Microsoft.AspNetCore.Identity;
using SvelteMongoBlog.Features;
using SvelteMongoBlog.Infrastructure;
using MongoDB.Bson;
using MongoDB.Driver;

namespace SvelteMongoBlog.Data;

/// <summary>Repeatable sample seed (sizes in <see cref="DemoSeedSize"/>); every phase checkpoints only after its transaction commits.</summary>
public sealed record DemoSeedSize(int Users = 78000, int Posts = 500000);

public static class DemoSeeder
{
    public static readonly string[] Topics = ["technology", "science", "design", "music", "travel", "food", "books", "gaming", "fitness", "nature", "photography", "film", "education", "business", "history", "art", "coding", "gardening", "sports", "community"];
    static readonly string[] Zones = ["UTC", "Europe/London", "America/New_York", "Asia/Kathmandu", "Australia/Lord_Howe", "Pacific/Auckland"];

    public const string Password = "Demo-42-correct-horse!";

    public static Task RunAsync(AppDb db, CancellationToken ct = default) => RunAsync(db, new DemoSeedSize(), ct);

    public static async Task RunAsync(AppDb db, DemoSeedSize size, CancellationToken ct = default)
    {
        var anchor = await db.SeedCheckpoints.Find(x => x.Name == "demo-42-anchor").FirstOrDefaultAsync(ct);
        if (anchor is null)
        {
            anchor = new SeedCheckpoint { Name = "demo-42-anchor", Seed = 42, UpdatedAt = DateTime.UtcNow.Date };
            await db.SeedCheckpoints.InsertOneAsync(anchor, cancellationToken: ct);
        }
        var now = DateTime.SpecifyKind(anchor.UpdatedAt, DateTimeKind.Utc);
        // One shared demo password: hash it once and reuse the hash (Identity verifies it against any user).
        var passwordHash = new PasswordHasher<User>().HashPassword(new User { Email = "", Name = "" }, Password);
        User MakeUser(Faker f, int i) => new() { DemoKey = $"demo-42-user-{i:D5}", Email = $"inertia.demo42.{i:D5}@yopmail.com", Name = $"{f.Name.FirstName()} {f.Name.LastName()}", PasswordHash = passwordHash, IsDemo = true,
            EmailVerifiedAt = now.AddDays(-120), CreatedAt = now.AddDays(-120), Preferences = new() { WeeklyDigest = false, EmailOnComment = false, TimeZoneId = Zones[i % Zones.Length] } };
        await Batch("users", size.Users, async (session, start, count) =>
        {
            var f = Fake("users", start);
            await db.Users.InsertManyAsync(session, Enumerable.Range(start, count).Select(i => MakeUser(f, i)).ToArray(), cancellationToken: ct);
        });
        var users = await db.Users.Find(x => x.DemoKey != null && x.DemoKey.StartsWith("demo-42-user-")).SortBy(x => x.DemoKey).Project(x => x.Id).ToListAsync(ct);
        if (users.Count != size.Users) throw new InvalidOperationException("Demo user set is incomplete; refusing relationship seed.");

        await Batch("posts", size.Posts, async (session, start, count) =>
        {
            var f = Fake("posts", start);
            var docs = Enumerable.Range(start, count).Select(i =>
            {
                var topic = Topics[i % Topics.Length]; var at = now.AddDays(-(i % 120 + 1)).AddSeconds(i % 86400);
                var title = Title(f); var body = Body(f, title, topic);
                return new Post { DemoKey = $"demo-42-post-{i:D6}", AuthorId = users[(i * 37 + 42) % users.Count], Title = title, Slug = $"demo-42-{topic}-{i:D6}",
                    Body = body,
                    Excerpt = PostsHelpers.MakeExcerpt(body), CreatedAt = at, PublishedAt = at, UpdatedAt = at, ArchivedAt = i % 97 == 0 ? now.AddHours(-1) : null, IndexVersion = 1 };
            }).ToArray();
            await db.Posts.InsertManyAsync(session, docs, cancellationToken: ct);
        });
        var posts = await db.Posts.Find(x => x.DemoKey != null && x.DemoKey.StartsWith("demo-42-post-")).SortBy(x => x.DemoKey).Project(x => x.Id).ToListAsync(ct);
        if (posts.Count != size.Posts) throw new InvalidOperationException("Demo post set is incomplete; refusing relationship seed.");

        await Batch("topics", size.Posts, (s, start, count) => Insert(db.PostTopics, Enumerable.Range(start, count).Select(i => new PostTopic { PostId = posts[i], Topic = Topics[i % 20] }), s, ct));
        await Batch("follows", size.Users * 5, (s, start, count) => Insert(db.Follows, Enumerable.Range(start, count).Select(i => new Follow { FollowerId = users[i % size.Users], FolloweeId = users[(i % size.Users + 1 + i / size.Users * 137) % size.Users], CreatedAt = now.AddDays(-60) }), s, ct));
        await Batch("likes", (int)Math.Min((long)size.Users * size.Posts, size.Posts * 39L / 10), async (session, start, count) =>
        {
            await Insert(db.Likes, Enumerable.Range(start, count).Select(i => new Like { PostId = posts[i % size.Posts], UserId = users[(i / size.Posts + i % size.Posts * 31 + 42) % size.Users], CreatedAt = now.AddDays(-(i % 90)).AddHours(-2) }), session, ct);
        });
        await Batch("comments", size.Posts * 3 / 2, async (session, start, count) =>
        {
            var f = Fake("comments", start);
            await Insert(db.Comments, Enumerable.Range(start, count).Select(i => new Comment { PostId = posts[i % size.Posts], UserId = users[(i * 17 + 42) % size.Users], Body = CommentBody(f), CreatedAt = now.AddDays(-(i % 90)).AddMinutes(-(i % 1440)) }), session, ct);
        });
        // Seeded comments sit in the seeded post/user id ranges (UUIDv7, time-ordered) and were created no later than the anchor; live ones are newer.
        var postLo = posts.Min(GuidByteOrder.Instance); var postHi = posts.Max(GuidByteOrder.Instance);
        var userLo = users.Min(GuidByteOrder.Instance); var userHi = users.Max(GuidByteOrder.Instance);
        var comments = await db.Comments.Find(x => x.CreatedAt <= now && x.PostId >= postLo && x.PostId <= postHi && x.UserId >= userLo && x.UserId <= userHi).SortBy(x => x.Id).Project(x => x.Id).ToListAsync(ct);
        if (comments.Count != size.Posts * 3 / 2) throw new InvalidOperationException("Demo comments are incomplete.");
        await Batch("comment-likes", size.Posts * 3, (s, start, count) => Insert(db.CommentLikes, Enumerable.Range(start, count).Select(i => new CommentLike { CommentId = comments[i % comments.Count], UserId = users[(i % comments.Count * 19 + i / comments.Count + 42) % size.Users], CreatedAt = now.AddDays(-(i % 60)) }), s, ct));
        await Batch("seen", size.Posts * 7, (s, start, count) => Insert(db.SeenPosts, Enumerable.Range(start, count).Select(i => { var at = now.AddDays(-(i % 90)).AddHours(-3); return new SeenPost { PostId = posts[i % size.Posts], UserId = users[(i % size.Posts * 23 + i / size.Posts + 42) % size.Users], FirstSeenAt = at, LastSeenAt = at }; }), s, ct));
        await Batch("bookmarks", size.Posts, (s, start, count) => Insert(db.Bookmarks, Enumerable.Range(start, count).Select(i => new Bookmark { PostId = posts[i], UserId = users[(i * 29 + 42) % size.Users], CreatedAt = now.AddDays(-(i % 30)) }), s, ct));
        await Batch("reposts", size.Posts / 2, async (session, start, count) =>
        {
            await Insert(db.Reposts, Enumerable.Range(start, count).Select(i => new Repost { PostId = posts[i], UserId = users[(i * 43 + 42) % size.Users], CreatedAt = now.AddDays(-(i % 30)) }), session, ct);
        });
        await Batch("profiles", size.Users, (s, start, count) => UpsertOnInsert(db.InterestProfiles,
            Enumerable.Range(start, count).Select(i => new InterestProfile { UserId = users[i], Dirty = true }), s, ct));
        await Batch("search", size.Posts, (s, start, count) => UpsertOnInsert(db.SearchIndexStates,
            Enumerable.Range(start, count).Select(i => new SearchIndexState { PostId = posts[i], RequestedVersion = 1 }), s, ct));
        await RefreshCounts(db, posts, ct);
        Console.WriteLine($"Demo seed 42 complete: {size.Users:N0} users, {size.Posts:N0} posts, and {size.Users * 5:N0} follows, {size.Posts * 39L / 10:N0} likes, {size.Posts * 3 / 2:N0} comments, {size.Posts * 3:N0} comment likes, {size.Posts * 7:N0} seen markers, {size.Posts:N0} bookmarks, {size.Posts / 2:N0} reposts.");

        async Task Batch(string name, int total, Func<IClientSessionHandle, int, int, Task> write)
        {
            var key = "demo-42-" + name; var checkpoint = await db.SeedCheckpoints.Find(x => x.Name == key).FirstOrDefaultAsync(ct); var start = checkpoint?.Completed ?? 0;
            while (start < total)
            {
                ct.ThrowIfCancellationRequested(); var count = Math.Min(500, total - start); var end = start + count;
                await db.TransactionAsync(async session =>
                {
                    // Collection writes and the progress marker share a transaction.
                    await write(session, start, count);
                    await db.SeedCheckpoints.UpdateOneAsync(session, x => x.Name == key,
                        Builders<SeedCheckpoint>.Update.SetOnInsert(x => x.Name, key).SetOnInsert(x => x.Seed, 42).Set(x => x.Completed, end).Set(x => x.UpdatedAt, DateTime.UtcNow),
                        new UpdateOptions { IsUpsert = true }, ct);
                }, ct);
                start = end; Console.WriteLine($"Seed {name}: {start:N0}/{total:N0}");
            }
        }
    }

    // Bogus content. Every 500-row batch gets its own generator seeded from (42, phase, batch start), so a resumed or repeated seed yields identical rows.
    static Faker Fake(string phase, int start) => new("en") { Random = new Randomizer(42 + start + phase.Aggregate(17, (h, c) => h * 31 + c)) };
    static string Paragraph(Faker f, int sentences) => f.Lorem.Sentences(sentences, " ");
    static string Title(Faker f) => f.Company.CatchPhrase();
    static string Body(Faker f, string title, string topic) =>
        $"# {title}\n\n{Paragraph(f, f.Random.Int(3, 5))}\n\n## Notes on {topic}\n\n{Paragraph(f, f.Random.Int(3, 5))}\n\n{Paragraph(f, f.Random.Int(2, 4))}";
    static string CommentBody(Faker f) => Paragraph(f, f.Random.Int(1, 3));

    static Task Insert<T>(IMongoCollection<T> collection, IEnumerable<T> docs, IClientSessionHandle session, CancellationToken ct) => collection.InsertManyAsync(session, docs, new InsertManyOptions { IsOrdered = true }, ct);

    static Task UpsertOnInsert<T>(IMongoCollection<T> collection, IEnumerable<T> documents, IClientSessionHandle session, CancellationToken ct)
    {
        var writes = documents.Select(document =>
        {
            var bson = document!.ToBsonDocument();
            var filter = new BsonDocumentFilterDefinition<T>(new BsonDocument("_id", bson["_id"]));
            var update = new BsonDocumentUpdateDefinition<T>(new BsonDocument("$setOnInsert", bson));
            return new UpdateOneModel<T>(filter, update) { IsUpsert = true };
        }).ToArray();
        return collection.BulkWriteAsync(session, writes, new BulkWriteOptions { IsOrdered = true }, ct);
    }

    static async Task RefreshCounts(AppDb db, IReadOnlyCollection<Guid> postIds, CancellationToken ct)
    {
        // UUIDv7 ids are time-ordered, so seeded ids sit in one narrow range (avoids a 500k-element $in). Mongo orders Standard UUIDs bytewise.
        var lo = postIds.Min(GuidByteOrder.Instance); var hi = postIds.Max(GuidByteOrder.Instance);
        var likes = await db.Likes.Aggregate().Match(x => x.PostId >= lo && x.PostId <= hi).Group(x => x.PostId, g => new { Id = g.Key, Count = g.Count() }).ToListAsync(ct);
        var comments = await db.Comments.Aggregate().Match(x => x.PostId >= lo && x.PostId <= hi).Group(x => x.PostId, g => new { Id = g.Key, Count = g.Count() }).ToListAsync(ct);
        var lm = likes.ToDictionary(x => x.Id, x => x.Count); var cm = comments.ToDictionary(x => x.Id, x => x.Count);
        foreach (var batch in postIds.Chunk(500))
        {
            var ops = batch.Select(id => new UpdateOneModel<Post>(Builders<Post>.Filter.Eq(x => x.Id, id), Builders<Post>.Update.Set(x => x.LikeCount, lm.GetValueOrDefault(id)).Set(x => x.CommentCount, cm.GetValueOrDefault(id)))).Cast<WriteModel<Post>>().ToArray();
            await db.Posts.BulkWriteAsync(ops, cancellationToken: ct);
        }
    }
}
