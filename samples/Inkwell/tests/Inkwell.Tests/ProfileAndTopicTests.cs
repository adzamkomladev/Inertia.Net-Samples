using System.Net;
using Inertia.Net.Testing;

namespace Inkwell.Tests;

/// Profile bios, the deferred author card on posts and topic browsing on the latest feed.
public class ProfileAndTopicTests(BlogFactory f) : IClassFixture<BlogFactory>
{
    private async Task<(BlogFactory.Client Client, Guid Id)> VerifiedClientAsync(string prefix = "p")
    {
        var email = BlogFactory.NewEmail(prefix);
        var id = await f.CreateUserAsync(email);
        var c = f.NewClient();
        await c.LoginOkAsync(email);
        return (c, id);
    }

    [Fact]
    public async Task Bio_is_saved_trimmed_shown_on_profile_and_validated()
    {
        var (c, id) = await VerifiedClientAsync();
        using (var saved = await c.SendAsync(HttpMethod.Put, "/settings/profile", new { displayName = "Bio Writer", bio = "  Essays on gardens.  " }))
            Assert.True(saved.StatusCode is HttpStatusCode.Redirect or HttpStatusCode.SeeOther or HttpStatusCode.OK, saved.StatusCode.ToString());

        using (var settings = await c.GetAsync("/settings"))
            await settings.AssertInertiaAsync(p => p.Where("profile.bio", "Essays on gardens."));
        using (var profile = await f.NewClient().GetAsync($"/users/{id}"))
            await profile.AssertInertiaAsync(p => p.Component("Users/Show").Where("user.bio", "Essays on gardens.").Has("user.joinedAt"));

        using (var tooLong = await c.SendAsync(HttpMethod.Put, "/settings/profile", new { displayName = "Bio Writer", bio = new string('x', 281) }))
            Assert.True(tooLong.StatusCode is HttpStatusCode.Redirect or HttpStatusCode.SeeOther or HttpStatusCode.UnprocessableEntity, tooLong.StatusCode.ToString());
        using (var unchanged = await c.GetAsync("/settings"))
            await unchanged.AssertInertiaAsync(p => p.Where("profile.bio", "Essays on gardens."));

        using (var cleared = await c.SendAsync(HttpMethod.Put, "/settings/profile", new { displayName = "Bio Writer", bio = "   " }))
            Assert.True(cleared.StatusCode is HttpStatusCode.Redirect or HttpStatusCode.SeeOther or HttpStatusCode.OK);
        using (var empty = await c.GetAsync("/settings"))
            await empty.AssertInertiaAsync(p => p.Where("profile.bio", (string?)null));
    }

    [Fact]
    public async Task Post_page_defers_the_author_card_with_follow_state()
    {
        var (author, authorId) = await VerifiedClientAsync("author");
        var postId = await author.CreatePostAsync("Card", "body");
        var (reader, _) = await VerifiedClientAsync("reader");

        var page = await (await reader.GetAsync($"/posts/{postId}")).GetInertiaPageAsync();
        new AssertableInertia(page).Component("Posts/Show").HasDeferred("author", "author").Missing("author");

        var loaded = await page.LoadDeferredPropsAsync(reader.Http);
        new AssertableInertia(loaded).Where("author.id", authorId).Where("author.following", false).Where("author.isSelf", false);
    }

    [Fact]
    public async Task Latest_feed_filters_by_topic_and_lists_popular_topics()
    {
        var (c, _) = await VerifiedClientAsync();
        var topic = "t" + Guid.NewGuid().ToString("N")[..12];
        using (var created = await c.PostAsync("/posts", new { title = "Tagged", body = "body", topics = new[] { topic } }))
            Assert.True(created.StatusCode is HttpStatusCode.Redirect or HttpStatusCode.SeeOther);
        await c.CreatePostAsync("Untagged", "body");

        using (var filtered = await c.GetAsync($"/posts?topic={topic.ToUpperInvariant()}"))
            await filtered.AssertInertiaAsync(p => p.Where("topic", topic).Has("posts.data", 1).Where("posts.data.0.title", "Tagged").Has("topics"));

        // An invalid topic is ignored rather than rejected, so shared links never break the page.
        using (var invalid = await c.GetAsync("/posts?topic=not%20valid!"))
            await invalid.AssertInertiaAsync(p => p.Where("topic", (string?)null));
    }
}
