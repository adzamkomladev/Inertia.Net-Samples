using CarParker.Data;
using CarParker.Features.Alerts;
using Microsoft.Extensions.DependencyInjection;
using MongoDB.Driver;

namespace CarParker.Tests;

public sealed class NotificationTests(AppFactory app) : IClassFixture<AppFactory>
{
    [Theory]
    [InlineData("reservation_soon", "occurrence:private-id", "Your parking window is coming up. Open your booking for arrival details.")]
    [InlineData("overstay", "session:private-id", "Your vehicle is still on site after its booked window. Please contact the gate team.")]
    [InlineData("refund", "Your refund was paid.", "Your refund was paid.")]
    public void Inbox_displays_readable_messages(string kind, string body, string expected)
    {
        Assert.Equal(expected, AlertsMappers.Row(new Alert { Kind = kind, Title = "Update", Body = body }).Body);
    }

    [Fact]
    public async Task Inbox_is_private_unread_by_default_and_retry_safe()
    {
        var (client, uid) = await app.SharedUserAsync();
        var (admin, adminId) = await app.SharedUserAsync(UserRole.Admin);
        await using var scope = app.Services.CreateAsyncScope();
        var svc = scope.ServiceProvider.GetRequiredService<AlertsService>();
        await svc.CreateAsync(uid, "refund", "Refund paid", "Your refund was paid.", sourceKey: "refund-test");
        await svc.CreateAsync(uid, "refund", "Refund paid", "Your refund was paid.", sourceKey: "refund-test");
        Assert.Equal(1, (await svc.SummaryAsync(uid, default)).UnreadCount);
        Assert.Empty((await svc.SummaryAsync(adminId, default)).Items);
        using var page = await app.InertiaGetAsync(client, "/lots");
        Assert.Contains("Refund paid", await page.Content.ReadAsStringAsync());
        using var other = await app.InertiaGetAsync(admin, "/admin");
        Assert.DoesNotContain("Refund paid", await other.Content.ReadAsStringAsync());
        await svc.MarkAllReadAsync(adminId, default);
        Assert.Equal(1, (await svc.SummaryAsync(uid, default)).UnreadCount);
        await svc.MarkAllReadAsync(uid, default);
        Assert.Equal(0, (await svc.SummaryAsync(uid, default)).UnreadCount);
        await svc.NotifyStaffAsync("Claim needs review", "/admin/insurance", default);
        Assert.Single((await svc.SummaryAsync(adminId, default)).Items);
        Assert.DoesNotContain((await svc.SummaryAsync(uid, default)).Items, n => n.Title == "Claim needs review");
        using var dashboard = await app.InertiaGetAsync(admin, "/account");
        Assert.Contains("account/Index", await dashboard.Content.ReadAsStringAsync());
    }
}
