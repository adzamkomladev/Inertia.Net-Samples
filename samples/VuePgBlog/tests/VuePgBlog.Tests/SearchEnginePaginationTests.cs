using Inertia.Net.Testing;
using VuePgBlog.Data;
using System.Net;

namespace VuePgBlog.Tests;

public class SearchEnginePaginationTests(AppFactory app) : IClassFixture<AppFactory>
{
    [Fact]
    public async Task Fallback_restarts_ranking_and_partial_response_keeps_engine()
    {
        var (client,owner) = await app.SignUpAsync("engine@yopmail.com");
        var term = "engine" + Guid.NewGuid().ToString("N");
        await app.QueryAsync(async db => {
            var at = DateTime.UtcNow;
            db.Posts.AddRange(Enumerable.Range(0,20).Select(i => new Post { AuthorId = owner,Title = term + " " + i,Slug = term + "-" + i,Body = "Search pagination fixture",Excerpt = "Search fixture",CreatedAt = at.AddMinutes(-i),UpdatedAt = at,PublishedAt = at.AddMinutes(-i) }));
            return await db.SaveChangesAsync();
        });
        using var first = await app.InertiaGetAsync(client,$"/posts?q={term}&page=1&engine=native");
        var baseline = await first.GetInertiaPageAsync();
        Assert.Equal("native",baseline.Prop("searchEngine")!.GetValue<string>());
        using var second = await app.InertiaGetAsync(client,$"/posts?q={term}&page=2&engine=native");
        Assert.Equal(2,(await second.GetInertiaPageAsync()).Prop("posts.page")!.GetValue<int>());
        client.DefaultRequestHeaders.Add("X-Inertia-Partial-Component","Posts/Index");
        client.DefaultRequestHeaders.Add("X-Inertia-Partial-Data","posts");
        using var changed = await app.InertiaGetAsync(client,$"/posts?q={term}&page=2&engine=meilisearch");
        Assert.Equal(HttpStatusCode.OK,changed.StatusCode);
        var restarted = await changed.GetInertiaPageAsync();
        Assert.Equal("native",restarted.Prop("searchEngine")!.GetValue<string>());
        Assert.Equal(1,restarted.Prop("posts.page")!.GetValue<int>());
        Assert.Equal(baseline.Prop("posts.data")!.ToJsonString(),restarted.Prop("posts.data")!.ToJsonString());
    }
}
