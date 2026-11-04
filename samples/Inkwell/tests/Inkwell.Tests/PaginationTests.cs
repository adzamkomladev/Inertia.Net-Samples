using System.Text.Json.Nodes;
using Inertia.Net.Testing;

namespace Inkwell.Tests;

/// Cursor pages honor the Inertia.Net 1.6.3 contract: `data` plus next/previous markers, `Scroll` metadata drives
/// the client's InfiniteScroll, and paging forward keeps the position (no overlap between pages).
public sealed class PaginationTests(BlogFactory f) : IClassFixture<BlogFactory>
{
    private async Task<BlogFactory.Client> SignedInAsync(string prefix)
    {
        var email = BlogFactory.NewEmail(prefix);
        await f.CreateUserAsync(email);
        var client = f.NewClient();
        await client.LoginOkAsync(email);
        return client;
    }

    [Fact]
    public async Task Latest_feed_pages_carry_cursor_metadata_and_page_forward()
    {
        var author = await SignedInAsync("p");
        for (var i = 0; i < 22; i++) await author.CreatePostAsync($"Paged post {i}", "body");

        using var first = await author.GetAsync("/posts");
        var page = await first.GetInertiaPageAsync();
        Assert.Equal(10, (int)page.Prop("posts.perPage")!);
        var firstId = page.Raw["props"]!["posts"]!["data"]![0]!["id"]!.ToString();

        // Paging forward keeps the stream position: the next ten posts follow the sent cursor.
        var second = await page.LoadNextPageAsync(author.Http, "posts");
        new AssertableInertia(second).HasScroll("posts").Has("posts.data", 10);
        Assert.Contains("cursor=", second.Url);
        Assert.NotEqual(firstId, second.Raw["props"]!["posts"]!["data"]![0]!["id"]!.ToString());
    }

    [Fact]
    public async Task People_directory_keysets_ascending_and_pages_forward()
    {
        for (var i = 0; i < 45; i++) await f.CreateUserAsync(BlogFactory.NewEmail("paged"));
        var client = f.NewClient();
        using var first = await client.GetAsync("/users");
        var page = await first.GetInertiaPageAsync();
        new AssertableInertia(page).HasScroll("users").Has("users.data", 20);

        var second = await page.LoadNextPageAsync(client.Http, "users");
        new AssertableInertia(second).Has("users.data", 20);
        var seen = (page.Raw["props"]!["users"]!["data"] as JsonArray)!.Select(u => u!["id"]!.ToString()).ToHashSet();
        foreach (var id in (second.Raw["props"]!["users"]!["data"] as JsonArray)!.Select(u => u!["id"]!.ToString()))
            Assert.DoesNotContain(id, seen);
    }
}
