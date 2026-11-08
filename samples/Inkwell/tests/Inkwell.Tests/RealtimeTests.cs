using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Inertia.Net.Testing;
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
        var denied = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await client.ConnectAsync(new Uri("ws://localhost/realtime"), timeout.Token));
        Assert.Contains("403", denied.Message);
    }

    [Fact]
    public async Task Same_origin_transport_delivers_public_counters_and_the_live_hub_rests_are_authorized()
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
        await using var guest = await RawConnection.Open(guestHttp);
        await using var author = await RawConnection.Open(ownerHttp);
        // The live transport is a browser's second WebSocket: its own session cookie, origin-matched connect.
        await using var authorLive = await LiveConnection.Open(app, await LoginCookieAsync(app, email), "private", $"users.{owner}");
        var realtime = app.Services.GetRequiredService<RealtimeEvents>();
        await realtime.ChangedAsync(postId, Guid.CreateVersion7(), "Someone liked your post", published: true);
        await DeliverQueuedAsync();
        var publicEvents = await guest.ReadUntil("NewPost");
        // The toast event still rides the raw hub; the badge refresh arrives on the live channel.
        Assert.Contains("Notification", await author.ReadUntil("Notification"));
        var liveEvents = await authorLive.ReadUntil("AnalyticsChanged");
        Assert.Contains("__inertia", liveEvents);
        Assert.DoesNotContain("AnalyticsChanged", publicEvents);
        Assert.DoesNotContain("Notification", publicEvents);
        using var frame = JsonDocument.Parse(publicEvents.Split('\u001e').First(x => x.Contains("PostChanged")));
        var counters = frame.RootElement.GetProperty("arguments")[0];
        Assert.Equal(postId, counters.GetProperty("id").GetGuid());
        Assert.Equal(0, counters.GetProperty("likeCount").GetInt32());
        Assert.False(counters.TryGetProperty("authorId", out _));
        await realtime.ChangedAsync(postId, owner, "Self-action must stay quiet");
        await DeliverQueuedAsync();
        var selfEvents = await authorLive.ReadUntil("AnalyticsChanged", 3000);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await author.ReadUntil("Notification", 800));
        await guest.ReadUntil("PostChanged");
        var likerEmail = BlogFactory.NewEmail("liker");
        await app.CreateUserAsync(likerEmail);
        var liker = app.NewClient();
        await liker.LoginOkAsync(likerEmail);
        using var liked = await liker.SendAsync(HttpMethod.Put, $"/posts/{postId}/like");
        Assert.True(liked.StatusCode is HttpStatusCode.Redirect or HttpStatusCode.SeeOther);
        await DeliverQueuedAsync();
        await authorLive.ReadUntil("AnalyticsChanged");
        Assert.Contains("Notification", await author.ReadUntil("Notification"));
        var likedEvents = await guest.ReadUntil("PostChanged");
        using var likedFrame = JsonDocument.Parse(likedEvents.Split('\u001e').First(x => x.Contains("PostChanged")));
        Assert.Equal(1, likedFrame.RootElement.GetProperty("arguments")[0].GetProperty("likeCount").GetInt32());
        using var unliked = await liker.SendAsync(HttpMethod.Delete, $"/posts/{postId}/like");
        Assert.True(unliked.StatusCode is HttpStatusCode.Redirect or HttpStatusCode.SeeOther);
        await DeliverQueuedAsync();
        await authorLive.ReadUntil("AnalyticsChanged");
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await author.ReadUntil("Notification", 800));
        var unlikedEvents = await guest.ReadUntil("PostChanged");
        using var unlikedFrame = JsonDocument.Parse(unlikedEvents.Split('\u001e').First(x => x.Contains("PostChanged")));
        Assert.Equal(0, unlikedFrame.RootElement.GetProperty("arguments")[0].GetProperty("likeCount").GetInt32());
        var viewer = app.NewClient();
        viewer.Http.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 TestBrowser");
        using var viewed = await viewer.PostAsync($"/posts/{postId}/view");
        Assert.Equal(HttpStatusCode.NoContent, viewed.StatusCode);
        await DeliverQueuedAsync();
        await authorLive.ReadUntil("AnalyticsChanged");
        var viewEvents = await guest.ReadUntil("PostChanged");
        using var viewFrame = JsonDocument.Parse(viewEvents.Split('\u001e').First(x => x.Contains("PostChanged")));
        Assert.Equal(1, viewFrame.RootElement.GetProperty("arguments")[0].GetProperty("viewCount").GetInt32());
        await realtime.RemovedAsync(postId, owner, true);
        await DeliverQueuedAsync();
        Assert.Contains("PostRemoved", await guest.ReadUntil("PostRemoved"));
        await authorLive.ReadUntil("AnalyticsChanged");
        await realtime.ChangedAsync(draftId, Guid.CreateVersion7(), "A draft must stay private", published: true);
        await DeliverQueuedAsync();
        // A draft change still validates the owner dashboards (cache invalidation fires), but nothing else is public.
        await authorLive.ReadUntil("AnalyticsChanged", 1500);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await author.ReadUntil("Notification", 800));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await guest.ReadUntil("PostChanged", 1000));
    }

    [Fact]
    public async Task Live_props_declare_the_owner_channel_and_the_hub_refuses_foreign_channels()
    {
        var email = BlogFactory.NewEmail("liveprops");
        var owner = await app.CreateUserAsync(email);
        var session = app.NewClient();
        await session.LoginOkAsync(email);

        // Shared and dashboard props listeners are on the owner's private channel.
        var page = await (await session.GetAsync("/analytics")).GetInertiaPageAsync();
        new AssertableInertia(page)
            .HasLive("notifications", $"private:users.{owner}", "Notification")
            .HasLive("summary", $"private:users.{owner}", "AnalyticsChanged")
            .HasLive("series", $"private:users.{owner}", "AnalyticsChanged")
            .HasLive("topPosts", $"private:users.{owner}", "AnalyticsChanged");

        // The live events reach a subscribed owner: dashboards reload from the publish, with no values attached.
        var postId = await session.CreatePostAsync("Live dashboard", "body");
        await using var authorLive = await LiveConnection.Open(app, await LoginCookieAsync(app, email), "private", $"users.{owner}");
        // Another signed-in user cannot subscribe to the owner's channel.
        var strangerEmail = BlogFactory.NewEmail("livestranger");
        await app.CreateUserAsync(strangerEmail);
        var stranger = app.NewClient();
        await stranger.LoginOkAsync(strangerEmail);
        await using var strangerLive = await LiveConnection.Open(app, await LoginCookieAsync(app, strangerEmail), "private", $"users.{owner}", allowed: false);
        await app.Services.GetRequiredService<RealtimeEvents>().ChangedAsync(postId, Guid.CreateVersion7(), "Someone liked your post");
        await DeliverQueuedAsync();
        await authorLive.ReadUntil("AnalyticsChanged");
        string strangerEvents = "";
        try { strangerEvents = await strangerLive.ReadUntil("__inertia", 800); }
        catch (OperationCanceledException) { }
        Assert.DoesNotContain("AnalyticsChanged", strangerEvents);
    }

    async Task DeliverQueuedAsync() => await app.DeliverJobsAsync("PostViewed", "PostChanged", "PostRemoved");

    sealed class RawConnection(HttpClient http, string url) : IAsyncDisposable
    {
        public static async Task<RawConnection> Open(HttpClient http)
        {
            using var negotiate = await Send(http, "/realtime/negotiate?negotiateVersion=1", "");
            negotiate.EnsureSuccessStatusCode();
            using var json = JsonDocument.Parse(await negotiate.Content.ReadAsStringAsync());
            var url = "/realtime?id=" + Uri.EscapeDataString(json.RootElement.GetProperty("connectionToken").GetString()!);
            using var initial = await http.GetAsync(url);
            initial.EnsureSuccessStatusCode();
            using var handshake = await Send(http, url, "{\"protocol\":\"json\",\"version\":1}\u001e");
            handshake.EnsureSuccessStatusCode();
            var result = new RawConnection(http, url);
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
                data += await response.Content.ReadAsStringAsync(timeout.Token);
                if (!response.IsSuccessStatusCode)
                    throw new HttpRequestException($"poll {(int)response.StatusCode}: {data}");
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

    // The /inertia-live hub over WebSockets: the session cookie goes on the connect request, channels are
    // Subscribe(type, name) invocations, and events arrive as inertia(channelKey, payload) frames.
    sealed class LiveConnection(WebSocket socket) : IAsyncDisposable
    {
        public static async Task<LiveConnection> Open(BlogFactory app, string cookieHeader, string type, string name, bool allowed = true)
        {
            var client = app.Server.CreateWebSocketClient();
            client.ConfigureRequest = request =>
            {
                request.Headers["Origin"] = "http://localhost";
                if (cookieHeader.Length > 0) request.Headers["Cookie"] = cookieHeader;
            };
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            WebSocket socket = await client.ConnectAsync(new Uri("ws://localhost/inertia-live"), timeout.Token);
            await SendAsync(socket, "{\"protocol\":\"json\",\"version\":1}\u001e");
            Assert.Contains("{}", await ReceiveUntilAsync(socket, "{}", timeout.Token));
            var id = "sub" + Guid.CreateVersion7().ToString("N");
            await SendAsync(socket, $"{{\"arguments\":[\"{type}\",\"{name}\"],\"invocationId\":\"{id}\",\"target\":\"Subscribe\",\"type\":1}}\u001e");
            var completion = await ReceiveUntilAsync(socket, $"\"invocationId\":\"{id}\"", timeout.Token);
            Assert.Contains(allowed ? "\"result\":true" : "\"result\":false", completion);
            return new LiveConnection(socket);
        }

        static async Task SendAsync(WebSocket socket, string frame)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            var bytes = Encoding.UTF8.GetBytes(frame);
            await socket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, timeout.Token);
        }

        static async Task<string> ReceiveUntilAsync(WebSocket socket, string target, CancellationToken timeout)
        {
            var data = "";
            var buffer = new byte[4096];
            while (!data.Contains(target))
            {
                var received = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), timeout);
                if (received.MessageType == WebSocketMessageType.Close) throw new IOException("The live socket closed.");
                data += Encoding.UTF8.GetString(buffer, 0, received.Count);
            }
            return data;
        }

        public async Task<string> ReadUntil(string target, int timeoutMilliseconds = 10000) =>
            await ReceiveUntilAsync(socket, target, new CancellationTokenSource(TimeSpan.FromMilliseconds(timeoutMilliseconds)).Token);

        public async ValueTask DisposeAsync() => await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, null, CancellationToken.None);
    }

    /// <summary>The auth cookies of a fresh session, as a Cookie header for the ws connect request.</summary>
    static async Task<string> LoginCookieAsync(BlogFactory app, string email)
    {
        var client = app.NewClient();
        using var login = await client.PostAsync("/login", new { email, password = "Passw0rd!x" });
        Assert.Equal("/posts", login.Headers.Location?.OriginalString);
        var cookies = login.Headers.TryGetValues("Set-Cookie", out var values) ? values : [];
        return string.Join("; ", cookies.Select(v => v.Split(';')[0]).Where(v => v.Contains('=')));
    }
}
