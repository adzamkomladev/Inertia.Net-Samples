using Inkwell.Data;
using Inkwell.Features.Discovery;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Inkwell.Tests;

public sealed class DiscoveryPersistenceTests(BlogFactory factory) : IClassFixture<BlogFactory>
{
    [Fact]
    public async Task For_you_uses_own_profile_and_following_uses_only_followee_profiles()
    {
        var viewer = await factory.CreateUserAsync(BlogFactory.NewEmail("viewer"));
        var followee = await factory.CreateUserAsync(BlogFactory.NewEmail("followee"));
        var ownSignalAuthor = await factory.CreateUserAsync(BlogFactory.NewEmail("own-signal-author"));
        var networkSignalAuthor = await factory.CreateUserAsync(BlogFactory.NewEmail("network-signal-author"));
        var ownCandidateAuthor = await factory.CreateUserAsync(BlogFactory.NewEmail("own-candidate-author"));
        var networkCandidateAuthor = await factory.CreateUserAsync(BlogFactory.NewEmail("network-candidate-author"));
        var now = DateTime.UtcNow;
        var ids = factory.WithDb(db =>
        {
            var ownSignal = AddPost(db, ownSignalAuthor, "Own signal", "body", now);
            var networkSignal = AddPost(db, networkSignalAuthor, "Network signal", "body", now);
            var ownCandidate = AddPost(db, ownCandidateAuthor, "Own match", "body", now);
            var networkCandidate = AddPost(db, networkCandidateAuthor, "Network match", "body", now);
            db.PostTopics.AddRange(
                new PostTopic { PostId = ownSignal.Id, Topic = "own-topic" },
                new PostTopic { PostId = networkSignal.Id, Topic = "network-topic" },
                new PostTopic { PostId = ownCandidate.Id, Topic = "own-topic" },
                new PostTopic { PostId = networkCandidate.Id, Topic = "network-topic" });
            db.PostLikes.AddRange(new PostLike { UserId = viewer, PostId = ownSignal.Id, CreatedAt = now },
                new PostLike { UserId = followee, PostId = networkSignal.Id, CreatedAt = now });
            db.Follows.Add(new Follow { FollowerId = viewer, FolloweeId = followee, CreatedAt = now });
            db.SaveChanges();
            return (ownCandidate.Id, networkCandidate.Id);
        });

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDb>();
        var discovery = new DiscoveryService(TimeProvider.System);
        var own = await discovery.GetPageAsync(db, viewer, DiscoveryFeedMode.ForYou, null, 20);
        var following = await discovery.GetPageAsync(db, viewer, DiscoveryFeedMode.FollowingInterests, null, 20);

        Assert.Contains(own.Items, item => item.PostId == ids.Item1);
        Assert.DoesNotContain(own.Items, item => item.PostId == ids.Item2);
        Assert.Contains(following.Items, item => item.PostId == ids.Item2);
        Assert.DoesNotContain(following.Items, item => item.PostId == ids.Item1);
    }

    [Fact]
    public async Task Seen_beacons_are_idempotent_and_remove_posts_from_a_stable_snapshot()
    {
        var viewer = await factory.CreateUserAsync(BlogFactory.NewEmail("seen-viewer"));
        var author = await factory.CreateUserAsync(BlogFactory.NewEmail("seen-author"));
        var now = DateTime.UtcNow;
        var postId = factory.WithDb(db =>
        {
            var signal = AddPost(db, author, "Liked post", "body", now);
            var candidate = AddPost(db, author, "Interest match", "body", now);
            db.PostTopics.AddRange(new PostTopic { PostId = signal.Id, Topic = "interest" }, new PostTopic { PostId = candidate.Id, Topic = "interest" });
            db.PostLikes.Add(new PostLike { UserId = viewer, PostId = signal.Id, CreatedAt = now });
            db.SaveChanges();
            return candidate.Id;
        });
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDb>();
        var discovery = new DiscoveryService(TimeProvider.System);
        var page = await discovery.GetPageAsync(db, viewer, DiscoveryFeedMode.ForYou, null, 10);
        Assert.Contains(page.Items, item => item.PostId == postId);

        await discovery.MarkSeenAsync(db, viewer, postId, now.AddMinutes(1));
        await discovery.MarkSeenAsync(db, viewer, postId, now);
        var seen = await db.SeenPosts.SingleAsync(row => row.UserId == viewer && row.PostId == postId);
        Assert.Equal(now.AddMinutes(1), seen.LastSeenAt);
        var next = await discovery.GetPageAsync(db, viewer, DiscoveryFeedMode.ForYou, page.NextCursor ?? $"{GetSnapshotId(db, viewer)}.1", 10);
        Assert.DoesNotContain(next.Items, item => item.PostId == postId);
    }

    static string GetSnapshotId(AppDb db, Guid userId) => db.FeedSnapshots.Where(row => row.UserId == userId && row.Mode == "for-you")
        .OrderByDescending(row => row.CreatedAt).Select(row => row.Id).First();

    static Post AddPost(AppDb db, Guid authorId, string title, string body, DateTime now)
    {
        var post = new Post { AuthorId = authorId, Title = title, Body = body, CreatedAt = now, PublishedAt = now, UpdatedAt = now };
        db.Posts.Add(post);
        db.SaveChanges();
        return post;
    }
}
