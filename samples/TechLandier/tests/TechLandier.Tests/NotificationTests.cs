using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TechLandier.Features;
using TechLandier.Infrastructure;

namespace TechLandier.Tests;

public sealed class NotificationTests(AppFactory app) : IClassFixture<AppFactory>
{
    [Fact]
    public async Task Personal_and_admin_notifications_remain_private_and_mark_read_is_scoped()
    {
        var (customer, uid) = await app.SignUpAsync("notice-customer@example.test");
        var (admin, adminId) = await app.AdminAsync("notice-admin@example.test");
        await app.QueryAsync(async db => { db.Alerts.Add(new Alert { UserId = uid, Kind = "order_status", Title = "Your order shipped", Body = "On its way", Href = "/account/orders" }); await db.SaveChangesAsync(); return true; });
        await using var scope = app.Services.CreateAsyncScope();
        var svc = scope.ServiceProvider.GetRequiredService<AlertsService>();
        Assert.Equal(1, (await svc.SummaryAsync(uid, default)).UnreadCount);
        Assert.Empty((await svc.SummaryAsync(adminId, default)).Items);
        await svc.MarkAllReadAsync(adminId, default);
        Assert.Equal(1, (await svc.SummaryAsync(uid, default)).UnreadCount);
        await scope.ServiceProvider.GetRequiredService<RealtimeEvents>().AdminPulseAsync("Action needed", "A ticket needs attention", "/admin/support");
        Assert.Single((await svc.SummaryAsync(adminId, default)).Items);
        await scope.ServiceProvider.GetRequiredService<RealtimeEvents>().StockAlertAsync(Guid.NewGuid(), 2);
        Assert.Contains((await svc.SummaryAsync(adminId, default)).Items, n => n.Title == "Low stock" && n.Href == "/admin/catalog");
        Assert.DoesNotContain((await svc.SummaryAsync(uid, default)).Items, n => n.Title == "Action needed");
        await svc.MarkAllReadAsync(uid, default);
        Assert.Equal(0, (await svc.SummaryAsync(uid, default)).UnreadCount);
        using var page = await app.InertiaGetAsync(customer, "/account");
        Assert.Contains("Your order shipped", await page.Content.ReadAsStringAsync());
    }
}
