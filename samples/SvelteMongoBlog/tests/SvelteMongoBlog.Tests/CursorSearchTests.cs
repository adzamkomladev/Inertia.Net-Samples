using System.Text.Json;
using MongoDB.Driver;
using SvelteMongoBlog.Data;

namespace SvelteMongoBlog.Tests;

public sealed class CursorSearchTests(AppFactory app) : IClassFixture<AppFactory>
{
    [Fact]
    public async Task Chronological_cursor_handles_ties_and_inserts_and_native_search_resets_engine()
    {
        var (_, authorId) = await app.SignUpAsync($"cursor-{Guid.NewGuid():N}@example.test");
        var token = "cursorprobe" + Guid.NewGuid().ToString("N");
        var at = DateTime.UtcNow.AddDays(1);
        var ids = await app.QueryAsync(async db =>
        {
            var rows = new List<Post>();
            var rowIds = AppFactory.OrderedIds(24);
            for (var i = 0; i < 24; i++) rows.Add(new Post { Id = rowIds[i], AuthorId = authorId,
                Title = $"Cursor {token}", Slug = token + i, Body = "Cursor test", Excerpt = "Cursor test", CreatedAt = at,
                PublishedAt = at, UpdatedAt = at, ArchivedAt = i == 0 ? at : null });
            await db.Posts.InsertManyAsync(rows);
            return rows.Select(x => x.Id).ToArray();
        });
        using var firstResponse = await app.InertiaGetAsync(app.Client(), "/posts");
        firstResponse.EnsureSuccessStatusCode();
        using var first = JsonDocument.Parse(await firstResponse.Content.ReadAsStringAsync());
        var firstPosts = first.RootElement.GetProperty("props").GetProperty("posts");
        var firstIds = firstPosts.GetProperty("data").EnumerateArray().Select(x => x.GetProperty("id").GetGuid()).ToArray();
        Assert.DoesNotContain(ids[0], firstIds);
        Assert.Equal(ids.Skip(14).Reverse(), firstIds);
        var cursor = firstPosts.GetProperty("nextCursor").GetString();
        var inserted = await app.QueryAsync(async db =>
        {
            var post = new Post { Id = Guid.CreateVersion7(), AuthorId = authorId, Title = $"Cursor {token}",
                Slug = token + "insert", Body = "Cursor test", Excerpt = "Cursor test", CreatedAt = at.AddMinutes(1), PublishedAt = at, UpdatedAt = at };
            await db.Posts.InsertOneAsync(post); return post.Id;
        });
        using var secondResponse = await app.InertiaGetAsync(app.Client(), "/posts?cursor=" + Uri.EscapeDataString(cursor!));
        secondResponse.EnsureSuccessStatusCode();
        using var second = JsonDocument.Parse(await secondResponse.Content.ReadAsStringAsync());
        var secondIds = second.RootElement.GetProperty("props").GetProperty("posts").GetProperty("data").EnumerateArray().Select(x => x.GetProperty("id").GetGuid()).ToArray();
        Assert.Empty(firstIds.Intersect(secondIds));
        Assert.DoesNotContain(inserted, secondIds);
        using var searchResponse = await app.InertiaGetAsync(app.Client(), "/posts?q=" + token + "&page=2&engine=meilisearch");
        searchResponse.EnsureSuccessStatusCode();
        using var search = JsonDocument.Parse(await searchResponse.Content.ReadAsStringAsync());
        Assert.Equal("native", search.RootElement.GetProperty("props").GetProperty("searchEngine").GetString());
        Assert.Equal(1, search.RootElement.GetProperty("props").GetProperty("posts").GetProperty("page").GetInt32());
    }
}