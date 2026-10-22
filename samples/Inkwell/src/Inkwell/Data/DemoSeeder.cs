using Bogus;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Inkwell.Data;

public sealed record DemoSeedSize(int Users = 78000, int Posts = 500000);

public static class DemoSeeder
{
    public static readonly string[] Topics = ["technology", "science", "design", "music", "travel", "food", "books", "gaming", "fitness", "nature", "photography", "film", "education", "business", "history", "art", "coding", "gardening", "sports", "community"];

    public const string Password = "Demo-42-correct-horse!";
    public static async Task RunAsync(AppDb db, CancellationToken ct = default) => await RunAsync(db, new DemoSeedSize(), ct);

    public static async Task RunAsync(AppDb db, DemoSeedSize size, CancellationToken ct = default)
    {
        db.Database.SetCommandTimeout(TimeSpan.Zero); // seed-time bulk statements exceed the 30s default at large sizes
        if (size.Users < 20 || size.Posts < 20) throw new ArgumentOutOfRangeException(nameof(size), "Seed at least 20 users and 20 posts.");
        var targetKey = $"demo-42-targets-{size.Users}-{size.Posts}";
        if (await db.SeedCheckpoints.AnyAsync(c => c.Name.StartsWith("demo-42-targets-") && c.Name != targetKey,ct))
            throw new InvalidOperationException("Demo seed target changed; use an empty database for different counts.");
        if (!await db.SeedCheckpoints.AnyAsync(c => c.Name == targetKey,ct))
        {
            db.SeedCheckpoints.Add(new SeedCheckpoint { Name = targetKey, Seed = 42 });
            await db.SaveChangesAsync(ct);
        }
        var anchor = await db.SeedCheckpoints.FindAsync(["demo-42-anchor"], ct);
        if (anchor is null)
        {
            anchor = new SeedCheckpoint { Name = "demo-42-anchor", Seed = 42, UpdatedAt = DateTime.UtcNow.Date };
            db.Add(anchor);
            await db.SaveChangesAsync(ct);
        }
        var now = DateTime.SpecifyKind(anchor.UpdatedAt, DateTimeKind.Utc);
        // One shared demo password: hash it once and reuse the hash (Identity verifies it against any user).
        var passwordHash = new PasswordHasher<AppUser>().HashPassword(new AppUser(), Password);
        AppUser CreateUser(Faker f, int i)
        {
            var email = $"inertia.demo42.{i:D5}@yopmail.com";
            return new AppUser {
                DemoKey = $"demo-42-user-{i:D5}",
                Email = email, UserName = email, NormalizedEmail = email.ToUpperInvariant(), NormalizedUserName = email.ToUpperInvariant(),
                DisplayName = $"{f.Name.FirstName()} {f.Name.LastName()}", IsDemo = true, EmailConfirmed = true, LockoutEnabled = true,
                PasswordHash = passwordHash, SecurityStamp = Guid.NewGuid().ToString("N"), CreatedAt = now.AddDays(-120),
                Preferences = new() { WeeklyDigest = false, EmailOnComment = false, EmailOnLike = false, DailyRecommendationDigest = false, TimeZoneId = Zones[i % Zones.Length] }
            };
        }
        await Batch("users", size.Users, async (f, start, end) => await Insert(Rows(start, end, i => CreateUser(f, i))));
        var users = await db.Users.AsNoTracking().Where(u => u.DemoKey != null && u.DemoKey.StartsWith("demo-42-user-")).OrderBy(u => u.DemoKey).Select(u => u.Id).ToArrayAsync(ct);
        if (users.Length != size.Users) throw new InvalidOperationException("Demo users changed; refusing to seed relationships against an incomplete set.");
        await Batch("posts", size.Posts, async (f, start, end) => await Insert(Rows(start, end, i =>
        {
            var topic = Topics[i % Topics.Length];
            var at = now.AddDays(-(i % 120 + 1)).AddSeconds(i % 86400);
            var title = Title(f);
            return new Post { DemoKey = $"demo-42-post-{i:D6}", AuthorId = users[(i * 37 + 42) % users.Length], Title = title, Body = Body(f, title, topic), CreatedAt = at, PublishedAt = at, UpdatedAt = at, ArchivedAt = i % 97 == 0 ? now.AddHours(-1) : null, IsPublished = i % 101 != 0 };
        })));
        var posts = await db.Posts.IgnoreQueryFilters().AsNoTracking().Where(p => p.DemoKey != null && p.DemoKey.StartsWith("demo-42-post-")).OrderBy(p => p.DemoKey).Select(p => p.Id).ToArrayAsync(ct);
        if (posts.Length != size.Posts) throw new InvalidOperationException("Demo posts changed; refusing incomplete seed.");
        await Batch("topics", size.Posts, async (f, start, end) => await Insert(Rows(start, end, i => new PostTopic { PostId = posts[i], Topic = Topics[i % Topics.Length] })));
        await Batch("follows", size.Users * 5, async (f, start, end) => await Insert(Rows(start, end, i => new Follow { FollowerId = users[i % size.Users], FolloweeId = users[(i % size.Users + 1 + i / size.Users) % size.Users], CreatedAt = now.AddDays(-60) })));
        await Batch("likes", (int)Math.Min((long)size.Users * size.Posts, size.Posts * 39L / 10), async (f, start, end) => await Insert(Rows(start, end, i => new PostLike { PostId = posts[i % size.Posts], UserId = users[(i / size.Posts + i % size.Posts * 31 + 42) % size.Users], CreatedAt = now.AddDays(-(i % 90)).AddHours(-2) })));
        await Batch("comments", size.Posts * 3 / 2, async (f, start, end) => await Insert(Rows(start, end, i => new Comment { PostId = posts[i % size.Posts], AuthorId = users[(i * 17 + 42) % size.Users], Body = CommentBody(f), CreatedAt = now.AddDays(-(i % 90)).AddMinutes(-i % 1440) })));
        // Seeded comments are the demo authors' comments created no later than the anchor; live ones are newer.
        var comments = await db.Comments.IgnoreQueryFilters().AsNoTracking().Where(c => c.CreatedAt <= now && c.Author.IsDemo).OrderBy(c => c.Id).Select(c => c.Id).ToArrayAsync(ct);
        if (comments.Length != size.Posts * 3 / 2) throw new InvalidOperationException("Demo comments changed; refusing incomplete seed.");
        await Batch("comment-likes", size.Posts * 3, async (f, start, end) => await Insert(Rows(start, end, i => new CommentLike { CommentId = comments[i % (size.Posts * 3 / 2)], UserId = users[(i % (size.Posts * 3 / 2) * 19 + i / (size.Posts * 3 / 2) + 42) % size.Users], CreatedAt = now.AddDays(-(i % 60)) })));
        await Batch("seen", size.Posts * 7, async (f, start, end) => await Insert(Rows(start, end, i => new SeenPost { PostId = posts[i % size.Posts], UserId = users[(i % size.Posts * 23 + i / size.Posts + 42) % size.Users], FirstSeenAt = now.AddDays(-(i % 90)).AddHours(-3), LastSeenAt = now.AddDays(-(i % 90)).AddHours(-3) })));
        await Batch("bookmarks", size.Posts, async (f, start, end) => await Insert(Rows(start, end, i => new Bookmark { PostId = posts[i], UserId = users[(i * 29 + 42) % size.Users], CreatedAt = now.AddDays(-(i % 30)) })));
        await Batch("reposts", size.Posts / 2, async (f, start, end) => await Insert(Rows(start, end, i => new Repost { PostId = posts[i], UserId = users[(i * 43 + 42) % size.Users], CreatedAt = now.AddDays(-(i % 30)) })));
        // Live workers may create these rows before the seed reaches this stage.
        await Batch("profiles", size.Users, async (f, start, end) => await Insert(Rows(start, end, i => new InterestProfile { UserId = users[i] }), ignoreConflicts: true));
        await db.Database.ExecuteSqlRawAsync("""UPDATE "Posts" SET "LikeCount" = (SELECT COUNT(*) FROM "PostLikes" WHERE "PostLikes"."PostId" = "Posts"."Id"), "CommentCount" = (SELECT COUNT(*) FROM "Comments" WHERE "Comments"."PostId" = "Posts"."Id") WHERE "DemoKey" LIKE 'demo-42-post-%';""", ct);
        await db.Database.ExecuteSqlRawAsync("ANALYZE;", ct);
        Console.WriteLine($"Demo seed 42 complete: {size.Users:N0} users, {size.Posts:N0} original posts including drafts and archives. Demo mail is captured locally.");

        async Task Insert<T>(IEnumerable<T> rows, bool ignoreConflicts = false) where T : class => await BulkInsert.InsertAsync(db, rows, ignoreConflicts, ct);

        async Task Batch(string name, int count, Func<Faker, int, int, Task> write)
        {
            var key = "demo-42-" + name;
            var progress = await db.SeedCheckpoints.AsNoTracking().SingleOrDefaultAsync(x => x.Name == key,ct);
            if (progress is not null && (progress.Seed != 42 || progress.Completed < 0 || progress.Completed > count))
                throw new InvalidOperationException($"Invalid demo checkpoint: {name}.");
            for (var start = progress?.Completed ?? 0; start < count; start += 500)
            {
                ct.ThrowIfCancellationRequested();
                await using var tx = await db.Database.BeginTransactionAsync(ct);
                var end = Math.Min(start + 500, count);
                await write(Fake(name, start), start, end);
                var checkpoint = await db.SeedCheckpoints.FindAsync([key], ct);
                if (checkpoint is null) { checkpoint = new() { Name = key, Seed = 42 }; db.Add(checkpoint); }
                checkpoint.Completed = end;
                checkpoint.UpdatedAt = DateTime.UtcNow;
                await db.SaveChangesAsync(ct);
                await tx.CommitAsync(ct);
                db.ChangeTracker.Clear();
                Console.WriteLine($"Seed {name}: {end:N0}/{count:N0}");
            }
        }
    }

    static IEnumerable<T> Rows<T>(int start, int end, Func<int, T> make) => Enumerable.Range(start, end - start).Select(make);

    // Bogus content. Every 500-row batch gets its own generator seeded from (42, phase, batch start), so a resumed or repeated seed yields identical rows.
    static Faker Fake(string phase, int start) => new("en") { Random = new Randomizer(42 + start + phase.Aggregate(17, (h, c) => h * 31 + c)) };
    static string Paragraph(Faker f, int sentences) => f.Lorem.Sentences(sentences, " ");
    static string Title(Faker f) => f.Company.CatchPhrase();
    static string Body(Faker f, string title, string topic) =>
        $"# {title}\n\n{Paragraph(f, f.Random.Int(3, 5))}\n\n## Notes on {topic}\n\n{Paragraph(f, f.Random.Int(3, 5))}\n\n{Paragraph(f, f.Random.Int(2, 4))}";
    static string CommentBody(Faker f) => Paragraph(f, f.Random.Int(1, 3));

    static readonly string[] Zones = ["UTC", "Europe/London", "America/New_York", "Asia/Kathmandu", "Australia/Lord_Howe", "Pacific/Auckland"];
}
