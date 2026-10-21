using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using SvelteMongoBlog.Features.Lots;
using SvelteMongoBlog.Infrastructure;

namespace SvelteMongoBlog.Tests;

public sealed class RealtimeTests(AppFactory app) : IClassFixture<AppFactory>
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
    public async Task Cross_origin_and_originless_negotiate_are_refused_but_spot_changes_are_broadcast()
    {
        var guestHttp = app.Client();
        using var bad = new HttpRequestMessage(HttpMethod.Post, "/realtime/negotiate?negotiateVersion=1");
        bad.Headers.Add("Origin", "https://another-site.example");
        using var denied = await guestHttp.SendAsync(bad);
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        using var missingOrigin = await guestHttp.PostAsync("/realtime/negotiate?negotiateVersion=1", null);
        Assert.Equal(HttpStatusCode.Forbidden, missingOrigin.StatusCode);

        var (lot, spots) = await app.SeedLotAsync("Realtime deck", spotCount: 1);
        await using var guest = await LiveConnection.Open(guestHttp);
        var realtime = app.Services.GetRequiredService<RealtimeEvents>();
        await realtime.SpotChangedAsync(lot.Id, spots[0].Id, "booked");

        var events = await guest.ReadUntil("SpotChanged");
        using var frame = JsonDocument.Parse(events.Split('\u001e').First(x => x.Contains("SpotChanged")));
        var change = frame.RootElement.GetProperty("arguments")[0];
        Assert.Equal(lot.Id, change.GetProperty("lotId").GetGuid());
        Assert.Equal(spots[0].Id, change.GetProperty("spotId").GetGuid());
        Assert.Equal("booked", change.GetProperty("state").GetString());
    }

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
        public async Task<string> ReadUntil(string target)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
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
