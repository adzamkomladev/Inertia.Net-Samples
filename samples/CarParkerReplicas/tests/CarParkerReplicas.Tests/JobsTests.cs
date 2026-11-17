using System.Net;
using Microsoft.Extensions.DependencyInjection;
using MongoDB.Driver;
using Inertia.Net.Testing;
using CarParker.Data;
using CarParker.Features.Bookings;
using CarParker.Infrastructure;

namespace CarParker.Tests;

/// Runwick jobs: ids-only payloads, repeat dispatches reuse the first job, and lists page.
public sealed class JobsTests(AppFactory app) : IClassFixture<AppFactory>
{
    [Fact]
    public async Task Verification_payload_carries_the_user_id_and_no_rendered_email()
    {
        var (_, userId) = await app.SignUpAndVerifyAsync("payload@example.test", verified: false);
        Assert.Equal("succeeded", await app.WaitForJobAsync("mail.verification", userId.ToString()));
        var payloads = await app.RunwickPayloadsAsync("mail.verification");
        var own = Assert.Single(payloads, p => p.Contains(userId.ToString()));
        Assert.DoesNotContain("<", own);
        Assert.DoesNotContain("http", own);
        Assert.DoesNotContain("payload@example.test", own);
    }

    [Fact]
    public async Task A_repeated_dispatch_reuses_the_job_for_the_same_key()
    {
        var refundId = Guid.NewGuid();
        await using var scope = app.Services.CreateAsyncScope();
        var jobs = scope.ServiceProvider.GetRequiredService<JobEnqueuer>();
        await jobs.DispatchAsync(new RefundPaidEmail(refundId));
        await jobs.DispatchAsync(new RefundPaidEmail(refundId));
        Assert.Equal(1, (await app.RunwickPayloadsAsync("mail.refund-paid")).Count(p => p.Contains(refundId.ToString())));
    }

    [Fact]
    public async Task Bookings_page_twenty_at_a_time_newest_first()
    {
        var (client, userId) = await app.SeedUserAsync();
        var (lot, spots) = await app.SeedLotAsync("Paging deck", spotCount: 1);
        var ids = AppFactory.OrderedIds(25);
        await app.QueryAsync(async db =>
        {
            await db.Reservations.InsertManyAsync(ids.Select((id, i) => new Reservation
            {
                Id = id, UserId = userId, LotId = lot.Id, SpotId = spots[0].Id, Status = ReservationStatus.Cancelled,
                CreatedAt = DateTime.UtcNow.AddMinutes(-60 + i), OccurrenceCount = 0,
            }));
            return true;
        });

        using var first = await app.InertiaGetAsync(client, "/account/bookings");
        var one = await first.GetInertiaPageAsync();
        Assert.Equal(20, one.Prop("bookings")!.AsArray().Count);
        Assert.Equal(1, one.Prop("page")!.GetValue<int>());
        Assert.True(one.Prop("hasMore")!.GetValue<bool>());
        Assert.Equal(ids[24], one.Prop("bookings.0.id")!.GetValue<Guid>());

        using var second = await app.InertiaGetAsync(client, "/account/bookings?page=2");
        var two = await second.GetInertiaPageAsync();
        Assert.Equal(5, two.Prop("bookings")!.AsArray().Count);
        Assert.False(two.Prop("hasMore")!.GetValue<bool>());
    }

    [Fact]
    public async Task The_demo_endpoints_need_an_admin()
    {
        var (customer, _) = await app.SeedUserAsync();
        using var denied = await customer.GetAsync("/demo/jobs");
        Assert.NotEqual(HttpStatusCode.OK, denied.StatusCode);
        var (admin, _) = await app.SeedUserAsync(UserRole.Admin);
        using var allowed = await admin.GetAsync("/demo/jobs");
        Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);
    }
}
