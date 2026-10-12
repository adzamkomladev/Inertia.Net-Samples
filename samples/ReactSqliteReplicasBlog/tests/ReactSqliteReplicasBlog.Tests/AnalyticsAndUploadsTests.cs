using System.Net;
using Inertia.Net.Testing;
using ReactSqliteReplicasBlog.Data;
using ReactSqliteReplicasBlog.Features.Uploads;
using Microsoft.EntityFrameworkCore;

namespace ReactSqliteReplicasBlog.Tests;

public class ImageSnifferTests
{
    private static byte[] Pad(params byte[] head) => [.. head, .. new byte[16]];

    [Fact]
    public void Png_and_jpeg_are_detected()
    {
        Assert.Equal(("image/png", "png"), ImageSniffer.Detect(Pad(0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A)));
        Assert.Equal(("image/jpeg", "jpg"), ImageSniffer.Detect(Pad(0xFF, 0xD8, 0xFF, 0xE0)));
    }

    [Fact]
    public void Gif_and_webp_are_detected()
    {
        Assert.Equal(("image/gif", "gif"), ImageSniffer.Detect("GIF89a...."u8));
        Assert.Equal(("image/gif", "gif"), ImageSniffer.Detect("GIF87a...."u8));
        Assert.Equal(("image/webp", "webp"), ImageSniffer.Detect("RIFF\0\0\0\0WEBPVP8 "u8));
    }

    [Fact]
    public void Text_svg_and_short_input_are_rejected()
    {
        Assert.Null(ImageSniffer.Detect("hello world, not an image"u8));
        Assert.Null(ImageSniffer.Detect("<svg xmlns=\"http://www.w3.org/2000/svg\"></svg>"u8));
        Assert.Null(ImageSniffer.Detect("RIFF\0\0\0\0WAVEfmt "u8));
        Assert.Null(ImageSniffer.Detect([]));
    }
}

public class AnalyticsTests(BlogFactory f) : IClassFixture<BlogFactory>
{
    private async Task<(BlogFactory.Client C, Guid Id)> VerifiedAsync()
    {
        var email = BlogFactory.NewEmail("an");
        var id = await f.CreateUserAsync(email);
        var c = f.NewClient();
        await c.LoginOkAsync(email);
        return (c, id);
    }

    [Fact]
    public async Task Overview_loads_deferred_summary_and_series()
    {
        var (c, _) = await VerifiedAsync();
        var postId = await c.CreatePostAsync("Tracked", "body");
        var today = DateOnly.FromDateTime(DateTime.UtcNow).ToString("yyyy-MM-dd");
        f.WithDb(db =>
        {
            for (var i = 0; i < 3; i++)
                db.Database.ExecuteSqlRaw("INSERT INTO PostViews (PostId, VisitorHash, Day) VALUES ({0}, {1}, {2})", postId, "h" + i, today);
            return 0;
        });

        var page = await (await c.GetAsync("/analytics")).GetInertiaPageAsync();
        new AssertableInertia(page).Component("Analytics/Index").Where("range", 30).HasDeferred("summary", "summary").HasDeferred("charts", "series").Missing("summary");

        var loaded = await page.LoadDeferredPropsAsync(c.Http);
        new AssertableInertia(loaded).Where("summary.views", 3).Where("summary.posts", 1).Has("series", 30).Has("topPosts");
    }

    [Fact]
    public async Task Other_users_post_analytics_is_forbidden()
    {
        var (owner, _) = await VerifiedAsync();
        var (other, _) = await VerifiedAsync();
        var id = await owner.CreatePostAsync("Private stats", "body");

        using var r = await other.GetAsync($"/posts/{id}/analytics");
        Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode);
        using var ok = await owner.GetAsync($"/posts/{id}/analytics");
        await ok.AssertInertiaAsync(p => p.Component("Posts/Analytics"));
    }

    [Fact]
    public async Task Anonymous_is_redirected_to_login()
    {
        using var r = await f.NewClient().GetAsync("/analytics");
        Assert.True(r.StatusCode is HttpStatusCode.Redirect or HttpStatusCode.SeeOther or HttpStatusCode.Unauthorized, r.StatusCode.ToString());
    }
}
