using System.Net;
using Inertia.Net.Testing;
using MongoDB.Driver;
using SvelteMongoBlog.Data;

namespace SvelteMongoBlog.Tests;

public sealed class TimelinePaginationTests(AppFactory app) : IClassFixture<AppFactory>
{
    [Fact]
    public async Task Timeline_modes_page_past_ties_and_skip_archived_bookmarks_and_reposts()
    {
        var (owner, ownerId) = await app.SignUpAsync($"timeline-owner-{Guid.NewGuid():N}@example.test");
        var (reader, readerId) = await app.SignUpAsync($"timeline-reader-{Guid.NewGuid():N}@example.test");
        using var ownerClient = owner;
        using var readerClient = reader;
        var publishedAt = new DateTime(2026, 9, 12, 8, 30, 0, DateTimeKind.Utc);
        var postIds = await app.QueryAsync(async db =>
        {
            var ids = AppFactory.OrderedIds(18);
            var posts = Enumerable.Range(0, 18).Select(i => new Post
            {
                Id = ids[i],
                AuthorId = ownerId,
                Title = $"Cursor post {i:D2}",
                Slug = $"cursor-post-{i:D2}-{Guid.NewGuid():N}",
                Body = "A timeline cursor test.",
                Excerpt = "A timeline cursor test.",
                CreatedAt = publishedAt,
                UpdatedAt = publishedAt,
                PublishedAt = publishedAt,
                ArchivedAt = i >= 12 ? publishedAt.AddDays(1) : null,
            }).ToArray();
            await db.Posts.InsertManyAsync(posts);

            var bookmarks = posts.Select(post => new Bookmark { UserId = readerId, PostId = post.Id, CreatedAt = publishedAt }).ToArray();
            await db.Bookmarks.InsertManyAsync(bookmarks);
            var repostIds = AppFactory.OrderedIds(posts.Length);
            var reposts = posts.Select((post, index) => new Repost { Id = repostIds[index], UserId = readerId, PostId = post.Id, CreatedAt = publishedAt }).ToArray();
            await db.Reposts.InsertManyAsync(reposts);
            await db.Users.UpdateManyAsync(x => x.Id == ownerId || x.Id == readerId,
                Builders<User>.Update.Set(x => x.Preferences.PostsPerPage, 5));
            return posts.Select(post => post.Id).ToArray();
        });

        Assert.Equal(postIds.Take(12).Reverse(), await ReadAllPostIds(ownerClient, "/me/posts"));

        Assert.Equal(postIds.Skip(12).Reverse(), await ReadAllPostIds(ownerClient, "/me/archives"));

        Assert.Equal(postIds.Take(12).Reverse(), await ReadAllPostIds(readerClient, "/bookmarks"));

        Assert.Equal(postIds.Take(12).Reverse(), await ReadAllPostIds(readerClient, "/me/posts"));
    }

    [Fact]
    public async Task User_summary_counts_all_incoming_and_outgoing_follows()
    {
        var (ownerClient, ownerId) = await app.SignUpAsync($"follow-count-owner-{Guid.NewGuid():N}@example.test");
        using var owner = ownerClient;
        var (viewer, viewerId) = await app.SignUpAsync($"follow-count-viewer-{Guid.NewGuid():N}@example.test");
        await app.QueryAsync(async db =>
        {
            var others = Enumerable.Range(0, 10).Select(i => new User
            {
                Email = $"follow-count-{Guid.NewGuid():N}@example.test",
                Name = $"Follow count user {i}",
            }).ToArray();
            await db.Users.InsertManyAsync(others);
            var edges = new List<Follow>
            {
                new() { FollowerId = viewerId, FolloweeId = ownerId },
            };
            edges.AddRange(others.Take(6).Select(user => new Follow { FollowerId = user.Id, FolloweeId = ownerId }));
            edges.AddRange(others.Skip(6).Take(4).Select(user => new Follow { FollowerId = ownerId, FolloweeId = user.Id }));
            await db.Follows.InsertManyAsync(edges);
            return true;
        });

        using var response = await app.InertiaGetAsync(viewer, $"/users/{ownerId}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var page = await response.GetInertiaPageAsync();
        Assert.Equal(7, page.Prop("user.followerCount")!.GetValue<int>());
        Assert.Equal(4, page.Prop("user.followingCount")!.GetValue<int>());
        Assert.True(page.Prop("user.following")!.GetValue<bool>());
    }

    async Task<Guid[]> ReadAllPostIds(HttpClient client, string path)
    {
        var ids = new List<Guid>();
        string? cursor = null;
        for (var pageNumber = 0; pageNumber < 10; pageNumber++)
        {
            var url = cursor is null ? path : $"{path}?cursor={Uri.EscapeDataString(cursor)}";
            using var response = await app.InertiaGetAsync(client, url);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var page = await response.GetInertiaPageAsync();
            ids.AddRange(page.Prop("posts.data")!.AsArray().Select(item => item!["id"]!.GetValue<Guid>()));
            cursor = page.Prop("posts.nextCursor")?.GetValue<string>();
            if (cursor is null) return ids.ToArray();
        }
        throw new Xunit.Sdk.XunitException($"Timeline at {path} did not finish within ten pages.");
    }
}
