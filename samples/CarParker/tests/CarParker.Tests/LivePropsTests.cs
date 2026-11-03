using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Inertia.Net.Testing;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using CarParker.Data;
using CarParker.Features;
using CarParker.Features.Alerts;
using CarParker.Infrastructure;

namespace CarParker.Tests;

/// <summary>
/// Live props deliver on the channels their listeners declare: the notification badge and the alerts
/// page on the owner's per-user channel, the guard/admin console props on the staff channel authorized
/// by role claims. Storefront Scroll props stay on the raw hub and have no live listeners.
/// </summary>
public sealed class LivePropsTests(AppFactory app) : IClassFixture<AppFactory>
{
    [Fact]
    public async Task Consoles_and_badges_declare_the_channels_they_listen_on()
    {
        var (admin, adminId) = await app.SeedUserAsync(UserRole.Admin);
        var (guard, guardId) = await app.SeedUserAsync(UserRole.Guard);
        var (customer, customerId) = await app.SeedUserAsync(UserRole.Customer);

        using var index = await app.InertiaGetAsync(admin, "/admin");
        new AssertableInertia(await index.GetInertiaPageAsync())
            .HasLive("notifications", $"private:users.{adminId}", "Notification")
            .HasLive("overview", "private:staff", "BoardChanged");

        using var analytics = await app.InertiaGetAsync(admin, "/admin/analytics");
        new AssertableInertia(await analytics.GetInertiaPageAsync())
            .HasLive("kpis", "private:staff", "BoardChanged")
            .HasLive("series", "private:staff", "BoardChanged")
            .HasLive("topLots", "private:staff", "BoardChanged")
            .HasLive("refundMix", "private:staff", "BoardChanged");

        using var console = await app.InertiaGetAsync(guard, "/guard");
        new AssertableInertia(await console.GetInertiaPageAsync())
            .HasLive("spots", "private:staff", "BoardChanged")
            .HasLive("queue", "private:staff", "BoardChanged")
            .HasLive("openSessions", "private:staff", "BoardChanged");

        using var alerts = await app.InertiaGetAsync(customer, "/account/alerts");
        new AssertableInertia(await alerts.GetInertiaPageAsync())
            .HasLive("alerts", $"private:users.{customerId}", "Notification");
    }

    [Fact]
    public async Task Handler_publishes_board_and_badge_events_and_the_hub_authorized_the_channels()
    {
        var adminEmail = $"admin-{Guid.NewGuid():N}@example.test";
        var (admin, adminId) = await app.SeedUserAsync(UserRole.Admin, adminEmail);
        var staffCookie = await CookieHeaderAsync(app, adminEmail);
        await using var adminSocket = await LiveConnection.Open(app, staffCookie, "private", "staff");

        var customerEmail = $"customer-{Guid.NewGuid():N}@example.test";
        var (customer, customerId) = await app.SeedUserAsync(UserRole.Customer, customerEmail);
        // Customers may subscribe to their own channel, never to the staff channel.
        await using var customerSocket = await LiveConnection.Open(app, await CookieHeaderAsync(app, customerEmail), "private", $"users.{customerId}");
        await using var refused = await LiveConnection.Open(app, await CookieHeaderAsync(app, customerEmail), "private", "staff", allowed: false);

        await using var scope = app.Services.CreateAsyncScope();
        var hub = scope.ServiceProvider.GetRequiredService<IHubContext<ParkHub>>();
        var live = scope.ServiceProvider.GetRequiredService<IInertiaLive>();
        var db = scope.ServiceProvider.GetRequiredService<AppDb>();
        var handler = new RealtimePushHandler(hub, live, db);

        await handler.HandleAsync(new RealtimePush("BoardChanged", LiveJson.Parse(new { kind = "session" }), ParkHub.StaffGroup), CancellationToken.None);
        var frame = await adminSocket.ReadUntil("__inertia");
        Assert.Contains("BoardChanged", frame);

        await handler.HandleAsync(new RealtimePush("Notification", LiveJson.Parse(new { message = "Your booking starts in one hour", href = "/account/bookings" }), UserId: customerId.ToString()), CancellationToken.None);
        Assert.Contains("Notification", await customerSocket.ReadUntil("Notification"));
    }

    static async Task<string> CookieHeaderAsync(AppFactory app, string email)
    {
        var client = app.Client();
        using var login = await app.SendAsync(client, "/login", new { email, password = AppFactory.Password });
        Assert.Equal(HttpStatusCode.Redirect, login.StatusCode);
        var cookies = login.Headers.TryGetValues("Set-Cookie", out var values) ? values : [];
        return string.Join("; ", cookies.Select(v => v.Split(';')[0]).Where(v => v.Contains('=')));
    }

    /// <summary>The /inertia-live hub over WebSockets: handshake, Subscribe(type, name), receive frames.</summary>
    sealed class LiveConnection : IAsyncDisposable
    {
        readonly WebSocket socket;

        LiveConnection(WebSocket socket) => this.socket = socket;

        public static async Task<LiveConnection> Open(AppFactory app, string cookieHeader, string type, string name, bool allowed = true)
        {
            var client = app.Server.CreateWebSocketClient();
            client.ConfigureRequest = request =>
            {
                request.Headers["Origin"] = "http://localhost";
                if (cookieHeader.Length > 0) request.Headers["Cookie"] = cookieHeader;
            };
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            WebSocket accepted = await client.ConnectAsync(new Uri("ws://localhost/inertia-live"), timeout.Token);
            await SendAsync(accepted, "{\"protocol\":\"json\",\"version\":1}\u001e");
            Assert.Contains("{}", await ReceiveUntilAsync(accepted, "{}", timeout.Token));
            var id = "sub" + Guid.CreateVersion7().ToString("N");
            await SendAsync(accepted, $"{{\"arguments\":[\"{type}\",\"{name}\"],\"invocationId\":\"{id}\",\"target\":\"Subscribe\",\"type\":1}}\u001e");
            var completion = await ReceiveUntilAsync(accepted, $"\"invocationId\":\"{id}\"", timeout.Token);
            Assert.Contains(allowed ? "\"result\":true" : "\"result\":false", completion);
            return new LiveConnection(accepted);
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

        public async Task<string> ReadUntil(string target, int timeoutMilliseconds = 15000) =>
            await ReceiveUntilAsync(socket, target, new CancellationTokenSource(TimeSpan.FromMilliseconds(timeoutMilliseconds)).Token);

        public async ValueTask DisposeAsync() => await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, null, CancellationToken.None);
    }
}

/// <summary>Small helper the live tests use to mint the SignalR payloads.</summary>
file static class LiveJson
{
    public static JsonElement Parse(object value) =>
        JsonSerializer.SerializeToElement(value, new JsonSerializerOptions(JsonSerializerDefaults.Web));
}
