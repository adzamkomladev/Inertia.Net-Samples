using System.Net;
using Microsoft.Extensions.Configuration;
using System.Text.Json.Nodes;
using Inertia.Net.Testing;
using MongoDB.Driver;
using SvelteMongoBlog.Data;

namespace SvelteMongoBlog.Tests;

/// AppFactory also sets plain APP_NAME="Plain Name"; an explicitly set App:Name must still win.
public sealed class CanonicalNameFactory : AppFactory
{
    protected override void ConfigureWebHost(Microsoft.AspNetCore.Hosting.IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.UseSetting("App:Name", "Canonical Name");
    }
}

public sealed class ConfigPrecedenceTests(CanonicalNameFactory app) : IClassFixture<CanonicalNameFactory>
{
    [Fact]
    public async Task Explicit_canonical_key_beats_plain_env_name()
    {
        using var response = await app.InertiaGetAsync(app.Client(), "/posts");
        await response.AssertInertiaAsync(page => page.Where("appName", "Canonical Name").Etc());
    }
}

public sealed class AppTests(AppFactory app) : IClassFixture<AppFactory>
{
    [Fact]
    public async Task Guests_can_browse_posts()
    {
        using var response = await app.InertiaGetAsync(app.Client(), "/posts");
        await response.AssertInertiaAsync(page => page
            .Component("Posts/Index")
            .Has("posts")
            .Where("filters.q", "")
            .WhereNull("auth.user")
            .Etc());
    }

    [Fact]
    public async Task Plain_env_names_override_config_keys()
    {
        using var response = await app.InertiaGetAsync(app.Client(), "/posts");
        await response.AssertInertiaAsync(page => page.Where("appName", "Plain Name").Etc());
    }

    [Fact]
    public async Task Health_endpoint_is_healthy()
    {
        using var response = await app.Client().GetAsync("/health");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Registration_errors_come_back_as_inertia_errors()
    {
        var client = app.Client();
        using var post = await app.SendAsync(client, "/register", new { name = "X", email = "not-an-email", password = "short", passwordConfirmation = "nope" });
        Assert.Equal(HttpStatusCode.Redirect, post.StatusCode);

        using var page = await app.InertiaGetAsync(client, "/register");
        await page.AssertInertiaAsync(p => p
            .Component("Auth/Register")
            .Has("errors.email")
            .Has("errors.password")
            .Etc());
    }

    [Fact]
    public async Task Unverified_users_cannot_write()
    {
        var (client, _) = await app.SignUpAsync("unverified@example.test", verified: false);
        using var response = await app.InertiaGetAsync(client, "/posts/create");
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.StartsWith("/email/verify", response.Headers.Location!.PathAndQuery);
    }

    [Fact]
    public async Task Guests_are_sent_to_login_from_protected_pages()
    {
        using var response = await app.InertiaGetAsync(app.Client(), "/settings/preferences");
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.StartsWith("/login", response.Headers.Location!.PathAndQuery);
    }

    [Fact]
    public async Task Verified_user_publishes_and_search_finds_it()
    {
        var (client, _) = await app.SignUpAsync("author@example.test");
        using var create = await app.SendAsync(client, "/posts", new { title = "Quokkas are delightful", body = "A post about **quokkas** and other marsupials." });
        Assert.Equal(HttpStatusCode.Redirect, create.StatusCode);
        Assert.StartsWith("/posts/quokkas-are-delightful", create.Headers.Location!.OriginalString);

        using var search = await app.InertiaGetAsync(app.Client(), "/posts?q=marsupials");
        await search.AssertInertiaAsync(p => p
            .Component("Posts/Index")
            .Has("posts.data", 1)
            .Where("posts.data.0.title", "Quokkas are delightful")
            .WhereNull("posts.data.0.snippet")
            .Etc());

        // FTS5 operators in user input must not reach MATCH (no 500).
        using var hostile = await app.InertiaGetAsync(app.Client(), "/posts?q=%22%20OR%20NEAR(a%20b)%20*");
        Assert.Equal(HttpStatusCode.OK, hostile.StatusCode);
    }

    [Fact]
    public async Task Only_the_owner_can_update_or_delete()
    {
        var (owner, _) = await app.SignUpAsync("owner@example.test");
        using (await app.SendAsync(owner, "/posts", new { title = "Owned post", body = "Mine." })) { }
        var postId = await app.PostIdAsync("Owned post");

        var (other, _) = await app.SignUpAsync("intruder@example.test");
        using var update = await app.SendAsync(other, $"/posts/{postId}", new { title = "Hijacked", body = "Not yours." }, HttpMethod.Put);
        Assert.Equal(HttpStatusCode.Forbidden, update.StatusCode);
        using var delete = await app.SendAsync(other, $"/posts/{postId}", new { }, HttpMethod.Delete);
        Assert.Equal(HttpStatusCode.Forbidden, delete.StatusCode);
        Assert.Equal("Owned post", await app.QueryAsync(db => db.Posts.Find(p => p.Id == postId).Project(p => p.Title).SingleAsync()));
    }

    [Fact]
    public async Task Likes_toggle_and_keep_counts_consistent()
    {
        var (author, _) = await app.SignUpAsync("liked@example.test");
        using (await app.SendAsync(author, "/posts", new { title = "Like me", body = "Please." })) { }
        var postId = await app.PostIdAsync("Like me");
        var likeCount = () => app.QueryAsync(db => db.Posts.Find(p => p.Id == postId).Project(p => p.LikeCount).SingleAsync());

        var (fan, _) = await app.SignUpAsync("fan@example.test");
        using (var like = await app.SendAsync(fan, $"/posts/{postId}/like", new { })) Assert.Equal(HttpStatusCode.Redirect, like.StatusCode);
        Assert.Equal(1, await likeCount());
        using (var unlike = await app.SendAsync(fan, $"/posts/{postId}/like", new { })) Assert.Equal(HttpStatusCode.Redirect, unlike.StatusCode);
        Assert.Equal(0, await likeCount());
    }

    [Fact]
    public async Task Requests_without_antiforgery_token_are_rejected()
    {
        using var response = await app.Client().PostAsync("/logout", null);
        Assert.Equal((HttpStatusCode)419, response.StatusCode);
    }

    [Fact]
    public async Task Verification_email_job_is_processed_by_the_worker()
    {
        await app.SignUpAsync("jobs@example.test", verified: false);
        var deadline = DateTime.UtcNow.AddSeconds(10);
        JobStatus? status = null;
        while (DateTime.UtcNow < deadline)
        {
            status = await app.QueryAsync(async db => (await db.Jobs.Find(j => j.Type == "SendEmail" && j.Payload.Contains("jobs@example.test")).FirstOrDefaultAsync())?.Status);
            if (status == JobStatus.Done) break;
            await Task.Delay(100);
        }
        Assert.Equal(JobStatus.Done, status);
    }

    [Fact]
    public async Task View_beacon_records_the_external_referrer_and_skips_the_author()
    {
        var (author, _) = await app.SignUpAsync("viewed@example.test");
        using (await app.SendAsync(author, "/posts", new { title = "Count my views", body = "Hello." })) { }
        var postId = await app.PostIdAsync("Count my views");

        // The author's own view is ignored.
        author.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Test)");
        using (var own = await app.SendAsync(author, $"/posts/{postId}/view", new { referrer = "https://news.example.com/a" }))
            Assert.Equal(HttpStatusCode.NoContent, own.StatusCode);

        var reader = app.Client();
        reader.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Test)");
        using (var view = await app.SendAsync(reader, $"/posts/{postId}/view", new { referrer = "https://news.example.com/a" }))
            Assert.Equal(HttpStatusCode.NoContent, view.StatusCode);
        using (var missing = await app.SendAsync(reader, "/posts/999999/view", new { }))
            Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);

        // ViewEventWriter flushes every 2s.
        var deadline = DateTime.UtcNow.AddSeconds(10);
        List<string?> referrers = [];
        while (DateTime.UtcNow < deadline && referrers.Count == 0)
        {
            await Task.Delay(250);
            referrers = await app.QueryAsync(db => db.PostViews.Find(v => v.PostId == postId).Project(v => v.Referrer).ToListAsync());
        }
        Assert.Equal(["news.example.com"], referrers);
    }

    [Fact]
    public void Presigned_urls_use_the_public_endpoint_while_server_calls_use_the_internal_one()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Storage:ServiceUrl"] = "http://s3:9000",
            ["Storage:PublicServiceUrl"] = "http://localhost:9000",
            ["Storage:Bucket"] = "uploads",
            ["Storage:AccessKey"] = "k",
            ["Storage:SecretKey"] = "s",
            ["Storage:Region"] = "us-east-1",
        }).Build();
        using var storage = new SvelteMongoBlog.Infrastructure.Storage(config);

        var put = new Uri(storage.PresignPut("u/1/x.png", "image/png", TimeSpan.FromMinutes(2)));
        Assert.Equal("localhost", put.Host);
        Assert.Equal(9000, put.Port);
        Assert.StartsWith("/uploads/u/1/x.png", put.AbsolutePath);
        Assert.Equal("localhost", new Uri(storage.Url("u/1/x.png")).Host); // private bucket: presigned GET, also public host
    }
}
