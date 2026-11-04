using System.Net.WebSockets;
using System.Text;
using Inertia.Net.Testing;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using TechLandier.Infrastructure;
using TechLandier.Data;

namespace TechLandier.Tests;

/// <summary>
/// Live props deliver on the channels their listeners declare: the notification bell on the owner's
/// per-user channel, admin dashboards on the admins channel, and order timelines on the public
/// per-order channel the tracking pages declare.
/// </summary>
public sealed class LivePropsTests(AppFactory app) : IClassFixture<AppFactory>
{
    [Fact]
    public async Task Notification_and_admin_props_declare_their_channels()
    {
        var (admin, adminId) = await app.AdminAsync("liveadmin@example.test");

        using var overview = await app.InertiaGetAsync(admin, "/admin");
        new AssertableInertia(await overview.GetInertiaPageAsync())
            .HasLive("notifications", $"private:users.{adminId}", "Notification")
            .HasLive("kpis", "private:admins", "AdminPulse")
            .HasLive("revenueSeries", "private:admins", "AdminPulse")
            .HasLive("attention", "private:admins", "AdminPulse");

        using var analytics = await app.InertiaGetAsync(admin, "/admin/analytics");
        new AssertableInertia(await analytics.GetInertiaPageAsync())
            .HasLive("series", "private:admins", "AdminPulse")
            .HasLive("topProducts", "private:admins", "AdminPulse")
            .HasLive("categoryShare", "private:admins", "AdminPulse");

        // The admin order board refreshes its listed page props too.
        using var orders = await app.InertiaGetAsync(admin, "/admin/orders");
        new AssertableInertia(await orders.GetInertiaPageAsync())
            .HasLive("orders", "private:admins", "AdminPulse");

        // The delivery board listens on the same channel.
        using var deliveries = await app.InertiaGetAsync(admin, "/admin/deliveries");
        new AssertableInertia(await deliveries.GetInertiaPageAsync())
            .HasLive("deliveries", "private:admins", "AdminPulse")
            .HasLive("riders", "private:admins", "AdminPulse")
            .HasLive("zones", "private:admins", "AdminPulse");

        // A distinct inbox (the handlers test owns livecustomer@example.test in the shared fixture).
        var (customer, customerId) = await app.SignUpAsync("livealerts@example.test");
        using var alerts = await app.InertiaGetAsync(customer, "/account/alerts");
        new AssertableInertia(await alerts.GetInertiaPageAsync())
            .HasLive("alerts", $"private:users.{customerId}", "Notification");
    }

    [Fact]
    public async Task Handlers_publish_to_the_channels_the_pages_listen_on()
    {
        var (admin, adminId) = await app.AdminAsync("livehandler@example.test");

        // The admins channel reaches a signed-in admin; a customer cannot join it.
        await using var adminSocket = await LiveConnection.Open(app, await CookieHeaderAsync(app, "livehandler@example.test"), "private", "admins");
        var (customer, customerId) = await app.SignUpAsync("livecustomer@example.test");
        await using var refused = await LiveConnection.Open(app, await CookieHeaderAsync(app, "livecustomer@example.test"), "private", "admins", allowed: false);
        // The customer socket uses their own signed-in session, as a browser page would.
        await using var customerSocket = await LiveConnection.Open(app, app.CookieHeader(customer), "private", $"users.{customerId}");

        await using var scope = app.Services.CreateAsyncScope();
        var hub = scope.ServiceProvider.GetRequiredService<IHubContext<StoreHub>>();
        var db = scope.ServiceProvider.GetRequiredService<AppDb>();
        var live = scope.ServiceProvider.GetRequiredService<IInertiaLive>();
        var handler = new RealtimePushHandler(hub, live, db);

        await handler.HandleAsync(new RealtimePush("OrderChanged", Json.Parse(new { orderNumber = "TL-X", status = "paid", paymentStatus = "settle" }), "admins"), CancellationToken.None);
        Assert.Contains("AdminPulse", await adminSocket.ReceiveUntil("AdminPulse"));

        await handler.HandleAsync(new RealtimePush("Notification", Json.Parse(new { title = "Installment received", body = "Part 1/3 paid.", href = "/account/credit" }), UserId: customerId.ToString()), CancellationToken.None);
        var frame = await customerSocket.ReceiveUntil("__inertia");
        Assert.Contains("Notification", frame);

        // Guests may join the public per-order channel.
        await using var guest = await LiveConnection.Open(app, "", "public", "orders.TL-X");
        await handler.HandleAsync(new RealtimePush("OrderTimelineUpdated", Json.Parse(new { orderNumber = "TL-X" }), UserId: customerId.ToString()), CancellationToken.None);
        Assert.Contains("OrderTimelineUpdated", await guest.ReceiveUntil("OrderTimelineUpdated"));
    }

    static async Task<string> CookieHeaderAsync(AppFactory app, string email)
    {
        var client = app.Client();
        using var login = await app.SendAsync(client, "/login", new { email, password = "correct-horse-battery" });
        // The live hub is a raw WebSocket, so the session cookie must come from the client's jar:
        // the redirect that follows the login POST carries no Set-Cookie of its own.
        return app.CookieHeader(client);
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
            WebSocket socket = await client.ConnectAsync(new Uri("ws://localhost/inertia-live"), timeout.Token);
            await SendAsync(socket, "{\"protocol\":\"json\",\"version\":1}\u001e");
            Assert.Contains("{}", await ReceiveUntilAsync(socket, "{}", timeout.Token));
            var id = "sub" + Guid.CreateVersion7().ToString("N");
            await SendAsync(socket, $"{{\"arguments\":[\"{type}\",\"{name}\"],\"invocationId\":\"{id}\",\"target\":\"Subscribe\",\"type\":1}}\u001e");
            var completion = await ReceiveUntilAsync(socket, $"\"invocationId\":\"{id}\"", timeout.Token);
            // A full frame on failure beats a string search: it shows exactly what the hub answered.
            Assert.True(completion.Contains(allowed ? "\"result\":true" : "\"result\":false"),
                $"Subscribe({type}, {name}) expected {(allowed ? "acceptance" : "refusal")}, got: {completion}");
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

        public async Task<string> ReceiveUntil(string target, int timeoutMilliseconds = 15000) =>
            await ReceiveUntilAsync(socket, target, new CancellationTokenSource(TimeSpan.FromMilliseconds(timeoutMilliseconds)).Token);

        public async ValueTask DisposeAsync() => await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, null, CancellationToken.None);
    }
}

/// <summary>Small helper the live tests use to mint the SignalR payloads.</summary>
file static class Json
{
    public static System.Text.Json.JsonElement Parse(object value) =>
        System.Text.Json.JsonSerializer.SerializeToElement(value, new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web));
}
