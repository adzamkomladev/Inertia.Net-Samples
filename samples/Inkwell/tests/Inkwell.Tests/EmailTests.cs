using System.Net;
using System.Text.Json;
using Inkwell.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Inkwell.Tests;

/// <summary>Captures what would go to the mail provider, so tests see the real request body and headers.</summary>
public sealed class CapturingMailHandler : HttpMessageHandler
{
    public List<JsonElement> Sent { get; } = [];

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        using var doc = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));
        lock (Sent) Sent.Add(doc.RootElement.Clone());
        return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("""{"data":{"reference":"test"}}""") };
    }
}

public sealed class EmailFactory : IDisposable
{
    public CapturingMailHandler Mail { get; } = new();
    public BlogFactory App { get; }

    public EmailFactory() => App = new BlogFactory(b =>
    {
        b.UseSetting("EMAIL_API_KEY", "test-key");
        b.ConfigureServices(s => s.AddHttpClient<Inkwell.Infrastructure.EmailSender>().ConfigurePrimaryHttpMessageHandler(() => Mail));
    });

    public void Dispose() => App.Dispose();
}

public class EmailTests(EmailFactory e) : IClassFixture<EmailFactory>
{
    BlogFactory F => e.App;

    async Task<(BlogFactory.Client Client, Guid Id)> UserAsync(string prefix, bool likeEmails = false)
    {
        var email = BlogFactory.NewEmail(prefix);
        var id = await F.CreateUserAsync(email);
        if (likeEmails)
            F.WithDb(db => db.Users.Where(u => u.Id == id).ExecuteUpdate(s => s.SetProperty(u => u.Preferences.EmailOnLike, true)));
        var c = F.NewClient();
        await c.LoginOkAsync(email);
        return (c, id);
    }

    List<JsonElement> MailTo(Guid userId)
    {
        var address = F.WithDb(db => db.Users.Where(u => u.Id == userId).Select(u => u.Email).Single());
        lock (e.Mail.Sent) return e.Mail.Sent.Where(m => m.GetProperty("to")[0].GetProperty("address").GetString() == address).ToList();
    }

    [Fact]
    public async Task Likes_are_summed_up_per_essay_and_skip_likes_the_author_already_read()
    {
        var (author, authorId) = await UserAsync("author", likeEmails: true);
        var post = await author.CreatePostAsync("Batched", "body");
        foreach (var fan in new[] { "ann", "ben" })
        {
            var (client, _) = await UserAsync(fan);
            using (await client.SendAsync(HttpMethod.Put, $"/posts/{post}/like")) { }
        }
        // Runwick keeps one pending job per essay; delivering the recorded ones runs that handler.
        await F.DeliverJobsAsync("LikeNotification");
        var first = MailTo(authorId);
        Assert.NotEmpty(first);
        Assert.Contains(" and ", first[^1].GetProperty("subject").GetString());

        using (await author.PostAsync("/notifications/read-all")) { }
        var (late, _) = await UserAsync("cat");
        using (await late.SendAsync(HttpMethod.Put, $"/posts/{post}/like")) { }
        await F.DeliverJobsAsync("LikeNotification");
        var last = MailTo(authorId)[^1].GetProperty("subject").GetString();
        Assert.StartsWith("cat", last);
        Assert.DoesNotContain(" and ", last);
    }

    [Fact]
    public async Task One_click_unsubscribe_is_a_POST_and_GET_only_asks()
    {
        var (author, authorId) = await UserAsync("owner", likeEmails: true);
        var post = await author.CreatePostAsync("Unsubscribable", "body");
        var (fan, _) = await UserAsync("fan");
        using (await fan.SendAsync(HttpMethod.Put, $"/posts/{post}/like")) { }
        await F.DeliverJobsAsync("LikeNotification");

        var headers = MailTo(authorId)[^1].GetProperty("headers");
        Assert.Equal("List-Unsubscribe=One-Click", headers.GetProperty("List-Unsubscribe-Post").GetString());
        var url = new Uri(headers.GetProperty("List-Unsubscribe").GetString()!.Trim('<', '>'));
        var http = F.CreateClient();

        using (var ask = await http.GetAsync(url.PathAndQuery))
        {
            Assert.Equal(HttpStatusCode.OK, ask.StatusCode);
            Assert.Contains("<form method=\"post\"", await ask.Content.ReadAsStringAsync());
        }
        Assert.True(F.WithDb(db => db.Users.Single(u => u.Id == authorId).Preferences.EmailOnLike));

        using (var done = await http.PostAsync(url.PathAndQuery, new FormUrlEncodedContent([new("List-Unsubscribe", "One-Click")])))
            Assert.Equal(HttpStatusCode.OK, done.StatusCode);
        Assert.False(F.WithDb(db => db.Users.AsNoTracking().Single(u => u.Id == authorId).Preferences.EmailOnLike));

        using var forged = await http.PostAsync("/email/unsubscribe?token=forged", null);
        Assert.Equal(HttpStatusCode.BadRequest, forged.StatusCode);
    }

    [Fact]
    public async Task Verification_mail_is_transactional_and_has_no_unsubscribe_header()
    {
        var email = BlogFactory.NewEmail("new");
        var id = await F.CreateUserAsync(email, verified: false);
        var c = F.NewClient();
        await c.LoginOkAsync(email);
        using (await c.PostAsync("/email/verification-notification")) { }
        await F.DeliverJobsAsync("SendVerificationEmail");
        var sent = MailTo(id);
        Assert.NotEmpty(sent);
        Assert.False(sent[^1].TryGetProperty("headers", out _));
        Assert.Contains("Confirm your email", sent[^1].GetProperty("html").GetString());
    }
}
