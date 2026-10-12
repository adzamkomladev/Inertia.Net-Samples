using ReactSqliteReplicasBlog.Cron;
using ReactSqliteReplicasBlog.Data;
using ReactSqliteReplicasBlog.Jobs;
using Microsoft.EntityFrameworkCore;

namespace ReactSqliteReplicasBlog.Tests;

public class CronTests
{
    private static AppUser User(string name, bool confirmed, DateTime createdAt, bool digest = true) => new()
    {
        UserName = name,
        Email = $"{name}@example.com",
        DisplayName = name,
        EmailConfirmed = confirmed,
        CreatedAt = createdAt,
        Preferences = new Preferences { WeeklyDigest = digest },
    };

    [Fact]
    public async Task Unverified_users_without_content_are_deleted()
    {
        await using var s = await TestServices.CreateAsync();
        var old = s.Now.AddDays(-8);
        await using (var db = s.NewDb())
        {
            var author = User("author", false, old);
            db.Users.AddRange(User("stale", false, old), User("recent", false, s.Now), User("confirmed", true, old), author);
            db.Posts.Add(new Post { AuthorId = author.Id, Title = "t", Body = "b", CreatedAt = old, UpdatedAt = old, ArchivedAt = old, IsPublished = false });
            await db.SaveChangesAsync();
        }

        await using (var db = s.NewDb())
            await CronTasks.UnverifiedUsersAsync(s.Provider, db, s.Now, default);

        await using (var db = s.NewDb())
            Assert.Equal(["author", "confirmed", "recent"], await db.Users.Select(u => u.UserName!).OrderBy(n => n).ToListAsync());
    }

    [Fact]
    public async Task Weekly_digest_enqueues_one_job_per_opted_in_confirmed_user()
    {
        await using var s = await TestServices.CreateAsync();
        await using (var db = s.NewDb())
        {
            db.Users.AddRange(User("yes", true, s.Now), User("optout", true, s.Now, digest: false), User("unconfirmed", false, s.Now));
            await db.SaveChangesAsync();
        }

        await using (var db = s.NewDb())
            await CronTasks.WeeklyDigestAsync(s.Provider, db, s.Now, default);

        await using (var db = s.NewDb())
        {
            var job = Assert.Single(await db.Jobs.ToListAsync());
            Assert.Equal(nameof(WeeklyDigestEmail), job.Type);
            var yes = await db.Users.SingleAsync(u => u.UserName == "yes");
            Assert.Contains(yes.Id.ToString(), job.Payload);
        }
    }

    [Fact]
    public async Task Prune_deletes_old_views_and_finished_jobs()
    {
        await using var s = await TestServices.CreateAsync();
        var today = DateOnly.FromDateTime(s.Now);
        await using (var db = s.NewDb())
        {
            var u = User("a", true, s.Now);
            var post = new Post { Author = u, AuthorId = u.Id, Title = "t", Body = "b" };
            db.Posts.Add(post);
            await db.SaveChangesAsync();
            db.PostViews.AddRange(
                new PostView { PostId = post.Id, VisitorHash = "old", Day = today.AddDays(-366) },
                new PostView { PostId = post.Id, VisitorHash = "new", Day = today.AddDays(-10) });
            Job J(JobStatus st, int daysAgo) => new() { Type = "X", Payload = "{}", Status = st, UpdatedAt = s.Now.AddDays(-daysAgo) };
            db.Jobs.AddRange(J(JobStatus.Done, 8), J(JobStatus.Done, 1), J(JobStatus.Failed, 31), J(JobStatus.Failed, 8), J(JobStatus.Pending, 100));
            await db.SaveChangesAsync();
        }

        await using (var db = s.NewDb())
            await CronTasks.PruneAsync(s.Provider, db, s.Now, default);

        await using (var db = s.NewDb())
        {
            Assert.Equal("new", Assert.Single(await db.PostViews.ToListAsync()).VisitorHash);
            Assert.Equal(3, await db.Jobs.CountAsync());
        }
    }
}
