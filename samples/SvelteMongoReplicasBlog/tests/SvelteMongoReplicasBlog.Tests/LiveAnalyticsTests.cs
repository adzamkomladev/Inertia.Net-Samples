using System.Text.Json;
using MongoDB.Driver;
using SvelteMongoReplicasBlog.Data;

namespace SvelteMongoReplicasBlog.Tests;

public sealed class LiveAnalyticsTests(AppFactory app) : IClassFixture<AppFactory>
{
    [Fact]
    public async Task Analytics_uses_committed_raw_events_for_kpis_charts_and_top_posts()
    {
        var (owner, userId) = await app.SignUpAsync($"analytics-{Guid.NewGuid():N}@example.test");
        var title = "Raw analytics " + Guid.NewGuid().ToString("N");
        using (await app.SendAsync(owner, "/posts", new { title, body = "Hello." })) { }
        var postId = await app.PostIdAsync(title);
        await app.QueryAsync(async db =>
        {
            var now = DateTime.UtcNow;
            await db.PostViews.InsertOneAsync(new PostView { Id = Guid.CreateVersion7(), PostId = postId, ViewerHash = "test-viewer", Referrer = "analytics.test", At = now });
            await db.Likes.InsertOneAsync(new Like { PostId = postId, UserId = userId, CreatedAt = now });
            return true;
        });
        Assert.Equal(0, await app.QueryAsync(db => db.PostDailyStats.CountDocumentsAsync(s => s.PostId == postId)));

        using var request = new HttpRequestMessage(HttpMethod.Get, "/analytics");
        request.Headers.Add("X-Inertia", "true");
        request.Headers.Add("X-Inertia-Version", await app.VersionAsync());
        request.Headers.Add("X-Inertia-Partial-Component", "Analytics/Index");
        request.Headers.Add("X-Inertia-Partial-Data", "kpis,series,topPosts");
        using var response = await owner.SendAsync(request);
        response.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var props = json.RootElement.GetProperty("props");
        var kpis = props.GetProperty("kpis");
        Assert.Equal(1, kpis.GetProperty("views").GetInt32());
        Assert.Equal(1, kpis.GetProperty("likes").GetInt32());
        var seriesValues = props.GetProperty("series");
        var series = seriesValues[seriesValues.GetArrayLength() - 1];
        Assert.Equal(1, series.GetProperty("views").GetInt32());
        Assert.Equal(1, series.GetProperty("likes").GetInt32());
        Assert.Equal(postId, props.GetProperty("topPosts")[0].GetProperty("id").GetGuid());

        using var postRequest = new HttpRequestMessage(HttpMethod.Get, $"/analytics/posts/{postId}");
        postRequest.Headers.Add("X-Inertia", "true");
        postRequest.Headers.Add("X-Inertia-Version", await app.VersionAsync());
        postRequest.Headers.Add("X-Inertia-Partial-Component", "Analytics/Post");
        postRequest.Headers.Add("X-Inertia-Partial-Data", "kpis,series,referrers");
        using var postResponse = await owner.SendAsync(postRequest);
        postResponse.EnsureSuccessStatusCode();
        using var postJson = JsonDocument.Parse(await postResponse.Content.ReadAsStringAsync());
        var postProps = postJson.RootElement.GetProperty("props");
        Assert.Equal(1, postProps.GetProperty("kpis").GetProperty("views").GetInt32());
        Assert.Equal(1, postProps.GetProperty("kpis").GetProperty("likes").GetInt32());
        Assert.Equal("analytics.test", postProps.GetProperty("referrers")[0].GetProperty("host").GetString());
    }
}
