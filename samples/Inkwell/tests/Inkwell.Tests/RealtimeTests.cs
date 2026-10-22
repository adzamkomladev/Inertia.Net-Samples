using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Inkwell.Infrastructure;

namespace Inkwell.Tests;

public sealed class RealtimeTests(BlogFactory app) : IClassFixture<BlogFactory>
{
    [Theory]
    [InlineData("wss", "https://localhost")]
    [InlineData("ws", "http://localhost")]
    public async Task Websocket_accepts_page_origin_with_forwarded_transport_scheme(string scheme, string origin)
    {
        var client = app.Server.CreateWebSocketClient();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        client.ConfigureRequest = request =>
        {
            request.Headers["Origin"] = origin;
            request.Headers["X-Forwarded-Proto"] = scheme;
        };
        using var socket = await client.ConnectAsync(new Uri("ws://localhost/realtime"), timeout.Token);
        await socket.SendAsync(Encoding.UTF8.GetBytes("{\"protocol\":\"json\",\"version\":1}\u001e"),
            System.Net.WebSockets.WebSocketMessageType.Text, true, timeout.Token);
        var buffer = new byte[1024];
        var received = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), timeout.Token);
        Assert.Contains("{}", Encoding.UTF8.GetString(buffer, 0, received.Count));
        socket.Abort();
        client.ConfigureRequest = request =>
        {
            request.Headers["Origin"] = "https://another-site.example";
            request.Headers["X-Forwarded-Proto"] = scheme;
        };
        var denied = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            client.ConnectAsync(new Uri("ws://localhost/realtime"), timeout.Token));
        Assert.Contains("403", denied.Message);
    }

    [Fact]
    public async Task Same_origin_transport_delivers_public_counters_and_private_owner_events()
    {
        var email = BlogFactory.NewEmail("realtime");
        var owner = await app.CreateUserAsync(email);
        var session = app.NewClient();
        await session.LoginOkAsync(email);
        var postId = await session.CreatePostAsync("Realtime post", "Persisted body");
        using var draftCreated = await session.PostAsync("/posts", new { title = "Private draft", body = "Not published", isPublished = false });
        var draftId = Guid.Parse(draftCreated.Headers.Location!.OriginalString.Split('/').Last());
        var ownerHttp = session.Http;
        var guestHttp = app.NewClient().Http;
        using var bad = new HttpRequestMessage(HttpMethod.Post, "/realtime/negotiate?negotiateVersion=1");
        bad.Headers.Add("Origin", "https://another-site.example");
        using var denied = await guestHttp.SendAsync(bad);
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        using var missingOrigin = await guestHttp.PostAsync("/realtime/negotiate?negotiateVersion=1", null);
        Assert.Equal(HttpStatusCode.Forbidden, missingOrigin.StatusCode);
        await using var guest = await LiveConnection.Open(guestHttp);
        await using var author = await LiveConnection.Open(ownerHttp);
        var realtime = app.Services.GetRequiredService<RealtimeEvents>();
        await realtime.ChangedAsync(postId, Guid.CreateVersion7(), "Someone liked your post", published: true);
        await DeliverQueuedAsync();
        var publicEvents = await guest.ReadUntil("NewPost");
        var privateEvents = await author.ReadUntil("AnalyticsChanged");
        Assert.Contains("PostChanged", publicEvents);
        Assert.DoesNotContain("AnalyticsChanged", publicEvents);
        Assert.DoesNotContain("Notification", publicEvents);
        Assert.Contains("Notification", privateEvents);
        Assert.Contains("Someone liked your post", privateEvents);
        using var frame = JsonDocument.Parse(publicEvents.Split('\u001e').First(x => x.Contains("PostChanged")));
        var counters = frame.RootElement.GetProperty("arguments")[0];
        Assert.Equal(postId, counters.GetProperty("id").GetGuid());
        Assert.Equal(0, counters.GetProperty("likeCount").GetInt32());
        Assert.False(counters.TryGetProperty("authorId", out _));
        await realtime.ChangedAsync(postId, owner, "Self-action must stay quiet");
        await DeliverQueuedAsync();
        var selfEvents = await author.ReadUntil("AnalyticsChanged");
        Assert.DoesNotContain("Notification", selfEvents);
        await guest.ReadUntil("PostChanged");
        var likerEmail = BlogFactory.NewEmail("liker");
        await app.CreateUserAsync(likerEmail);
        var liker = app.NewClient();
        await liker.LoginOkAsync(likerEmail);
        using var liked = await liker.SendAsync(HttpMethod.Put, $"/posts/{postId}/like");
        Assert.True(liked.StatusCode is HttpStatusCode.Redirect or HttpStatusCode.SeeOther);
        await DeliverQueuedAsync();
        Assert.Contains("Notification", await author.ReadUntil("AnalyticsChanged"));
        var likedEvents = await guest.ReadUntil("PostChanged");
        using var likedFrame = JsonDocument.Parse(likedEvents.Split('\u001e').First(x => x.Contains("PostChanged")));
        Assert.Equal(1, likedFrame.RootElement.GetProperty("arguments")[0].GetProperty("likeCount").GetInt32());
        using var unliked = await liker.SendAsync(HttpMethod.Delete, $"/posts/{postId}/like");
        Assert.True(unliked.StatusCode is HttpStatusCode.Redirect or HttpStatusCode.SeeOther);
        await DeliverQueuedAsync();
        Assert.DoesNotContain("Notification", await author.ReadUntil("AnalyticsChanged"));
        var unlikedEvents = await guest.ReadUntil("PostChanged");
        using var unlikedFrame = JsonDocument.Parse(unlikedEvents.Split('\u001e').First(x => x.Contains("PostChanged")));
        Assert.Equal(0, unlikedFrame.RootElement.GetProperty("arguments")[0].GetProperty("likeCount").GetInt32());
        var viewer = app.NewClient();
        viewer.Http.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 TestBrowser");
        using var viewed = await viewer.PostAsync($"/posts/{postId}/view");
        Assert.Equal(HttpStatusCode.NoContent, viewed.StatusCode);
        await DeliverQueuedAsync();
        await author.ReadUntil("AnalyticsChanged");
        var viewEvents = await guest.ReadUntil("PostChanged");
        using var viewFrame = JsonDocument.Parse(viewEvents.Split('\u001e').First(x => x.Contains("PostChanged")));
        Assert.Equal(1, viewFrame.RootElement.GetProperty("arguments")[0].GetProperty("viewCount").GetInt32());
        await realtime.RemovedAsync(postId, owner, true);
        await DeliverQueuedAsync();
        Assert.Contains("PostRemoved", await guest.ReadUntil("PostRemoved"));
        await author.ReadUntil("AnalyticsChanged");
        await realtime.ChangedAsync(draftId, Guid.CreateVersion7(), "A draft must stay private", published: true);
        await DeliverQueuedAsync();
        var draftEvents = await author.ReadUntil("AnalyticsChanged");
        Assert.DoesNotContain("Notification", draftEvents);
        Assert.DoesNotContain("PostChanged", draftEvents);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => guest.ReadUntil("PostChanged", 1000));
    }

    Task DeliverQueuedAsync() => app.DeliverJobsAsync("PostViewed", "PostChanged", "PostRemoved");

    sealed class LiveConnection(HttpClient http, string url) : IAsyncDisposable
    {
        public static async Task<LiveConnection> Open(HttpClient http)
        {
            using var negotiate = await Send(http, "/realtime/negotiate?negotiateVersion=1", "");
            negotiate.EnsureSuccessStatusCode();
            using var json = JsonDocument.Parse(await negotiate.Content.ReadAsStringAsync());
            var url = "/realtime?id=" + Uri.EscapeDataString(json.RootElement.GetProperty("connectionToken").GetString()!);
            using var initial = await http.GetAsync(url);
            initial.EnsureSuccessStatusCode();
            using var handshake = await Send(http, url, "{\"protocol\":\"json\",\"version\":1}\u001e");
            handshake.EnsureSuccessStatusCode();
            var result = new LiveConnection(http, url);
            Assert.Contains("{}", await result.ReadUntil("{}"));
            return result;
        }
        static async Task<HttpResponseMessage> Send(HttpClient http, string url, string body)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = new StringContent(body, Encoding.UTF8, "text/plain") };
            request.Headers.Add("Origin", http.BaseAddress!.GetLeftPart(UriPartial.Authority));
            return await http.SendAsync(request);
        }
        public async Task<string> ReadUntil(string target, int timeoutMilliseconds = 10000)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromMilliseconds(timeoutMilliseconds));
            var data = "";
            while (!data.Contains(target))
            {
                using var response = await http.GetAsync(url, timeout.Token);
                response.EnsureSuccessStatusCode();
                data += await response.Content.ReadAsStringAsync(timeout.Token);
            }
            return data;
        }
        public async ValueTask DisposeAsync()
        {
            using var request = new HttpRequestMessage(HttpMethod.Delete, url);
            request.Headers.Add("Origin", http.BaseAddress!.GetLeftPart(UriPartial.Authority));
            using var response = await http.SendAsync(request);
        }
    }
}
