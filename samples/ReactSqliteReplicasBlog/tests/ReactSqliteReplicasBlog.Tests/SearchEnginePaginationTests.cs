using Inertia.Net.Testing;
using ReactSqliteReplicasBlog.Data;
using System.Net;

namespace ReactSqliteReplicasBlog.Tests;

public class SearchEnginePaginationTests(BlogFactory f) : IClassFixture<BlogFactory>
{
    [Fact]
    public async Task Fallback_restarts_ranking_and_partial_response_keeps_engine()
    {
        var owner = await f.CreateUserAsync(BlogFactory.NewEmail("engine"));
        var term = "engine" + Guid.NewGuid().ToString("N");
        f.WithDb(db => {
            var at = DateTime.UtcNow;
            db.Posts.AddRange(Enumerable.Range(0,20).Select(i => new Post { AuthorId = owner,Title = term + " " + i,Body = "Search pagination fixture",CreatedAt = at.AddMinutes(-i),UpdatedAt = at,PublishedAt = at.AddMinutes(-i) }));
            return db.SaveChanges();
        });
        var c = f.NewClient();
        using var first = await c.GetAsync($"/posts?q={term}&page=1&engine=native");
        var baseline = await first.GetInertiaPageAsync();
        Assert.Equal("native",baseline.Prop("searchEngine")!.GetValue<string>());
        using var second = await c.GetAsync($"/posts?q={term}&page=2&engine=native");
        Assert.Equal(2,(await second.GetInertiaPageAsync()).Prop("posts.page")!.GetValue<int>());
        c.Http.DefaultRequestHeaders.Add("X-Inertia-Partial-Component","Posts/Index");
        c.Http.DefaultRequestHeaders.Add("X-Inertia-Partial-Data","posts");
        using var changed = await c.GetAsync($"/posts?q={term}&page=2&engine=meilisearch");
        Assert.Equal(HttpStatusCode.OK,changed.StatusCode);
        var restarted = await changed.GetInertiaPageAsync();
        Assert.Equal("native",restarted.Prop("searchEngine")!.GetValue<string>());
        Assert.Equal(1,restarted.Prop("posts.page")!.GetValue<int>());
        Assert.Equal(baseline.Prop("posts.data")!.ToJsonString(),restarted.Prop("posts.data")!.ToJsonString());
    }
}
