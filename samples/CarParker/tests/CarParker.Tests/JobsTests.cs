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
    public async Task Bookings_split_into_upcoming_and_past_and_page_twenty_at_a_time()
    {
        var (client, userId) = await app.SeedUserAsync();
        var (lot, spots) = await app.SeedLotAsync("Paging lot", spotCount: 1);
        var now = DateTime.UtcNow;

        // Past: 25 cancelled or ended bookings; the list reads latest end first whatever the creation order.
        var pastIds = AppFactory.OrderedIds(25);
        var past = pastIds.Select((id, i) => new Reservation
        {
            Id = id, UserId = userId, LotId = lot.Id, SpotId = spots[0].Id,
            Status = i % 2 == 0 ? ReservationStatus.Cancelled : ReservationStatus.Confirmed,
            CreatedAt = now.AddDays(-60).AddMinutes(i), StartAtUtc = now.AddDays(-30 + i).AddHours(-2), EndAtUtc = now.AddDays(-30 + i),
            OccurrenceCount = 1,
        }).ToList();

        // Upcoming: 21 singles created in the opposite order of their start, plus one weekly booking that started last
        // week and whose next window is the soonest of all.
        var upcomingIds = AppFactory.OrderedIds(21);
        var singles = upcomingIds.Select((id, i) => new Reservation
        {
            Id = id, UserId = userId, LotId = lot.Id, SpotId = spots[0].Id, Status = ReservationStatus.Confirmed,
            CreatedAt = now.AddMinutes(-30 + i), StartAtUtc = now.AddHours(2 + 21 - i), EndAtUtc = now.AddHours(3 + 21 - i),
            OccurrenceCount = 1,
        }).ToList();
        var weekly = new Reservation
        {
            UserId = userId, LotId = lot.Id, SpotId = spots[0].Id, Status = ReservationStatus.Confirmed, WindowType = WindowType.Recurring,
            CreatedAt = now.AddDays(-8), StartAtUtc = now.AddDays(-7), EndAtUtc = now.AddDays(7).AddHours(1), OccurrenceCount = 3,
        };
        var occurrences = singles.Select(r => new Occurrence { ReservationId = r.Id, StartAtUtc = r.StartAtUtc!.Value, EndAtUtc = r.EndAtUtc!.Value })
            .Concat([
                new Occurrence { ReservationId = weekly.Id, Index = 0, StartAtUtc = now.AddDays(-7), EndAtUtc = now.AddDays(-7).AddHours(1), Status = OccurrenceStatus.Done },
                new Occurrence { ReservationId = weekly.Id, Index = 1, StartAtUtc = now.AddMinutes(30), EndAtUtc = now.AddMinutes(90) },
                new Occurrence { ReservationId = weekly.Id, Index = 2, StartAtUtc = now.AddDays(7), EndAtUtc = now.AddDays(7).AddHours(1) },
            ]).ToList();
        await app.QueryAsync(async db =>
        {
            await db.Reservations.InsertManyAsync([.. past, .. singles, weekly]);
            await db.Occurrences.InsertManyAsync(occurrences);
            return true;
        });

        using var first = await app.InertiaGetAsync(client, "/account/bookings");
        var one = await first.GetInertiaPageAsync();
        Assert.Equal("upcoming", one.Prop("view")!.GetValue<string>());
        Assert.Equal(20, one.Prop("bookings")!.AsArray().Count);
        Assert.Equal(1, one.Prop("page")!.GetValue<int>());
        Assert.True(one.Prop("hasMore")!.GetValue<bool>());
        Assert.Equal(weekly.Id, one.Prop("bookings.0.id")!.GetValue<Guid>());
        Assert.Equal(upcomingIds[20], one.Prop("bookings.1.id")!.GetValue<Guid>());
        Assert.DoesNotContain(one.Prop("bookings")!.AsArray(), b => pastIds.Contains(b!["id"]!.GetValue<Guid>()));

        using var second = await app.InertiaGetAsync(client, "/account/bookings?view=upcoming&page=2");
        var two = await second.GetInertiaPageAsync();
        Assert.Equal(2, two.Prop("bookings")!.AsArray().Count);
        Assert.False(two.Prop("hasMore")!.GetValue<bool>());
        Assert.Equal(upcomingIds[0], two.Prop("bookings.1.id")!.GetValue<Guid>());

        using var third = await app.InertiaGetAsync(client, "/account/bookings?view=past");
        var three = await third.GetInertiaPageAsync();
        Assert.Equal("past", three.Prop("view")!.GetValue<string>());
        Assert.Equal(20, three.Prop("bookings")!.AsArray().Count);
        Assert.True(three.Prop("hasMore")!.GetValue<bool>());
        Assert.Equal(pastIds[24], three.Prop("bookings.0.id")!.GetValue<Guid>());
        Assert.Equal(pastIds[23], three.Prop("bookings.1.id")!.GetValue<Guid>());

        using var fourth = await app.InertiaGetAsync(client, "/account/bookings?view=past&page=2");
        var four = await fourth.GetInertiaPageAsync();
        Assert.Equal(5, four.Prop("bookings")!.AsArray().Count);
        Assert.False(four.Prop("hasMore")!.GetValue<bool>());
        Assert.Equal(pastIds[0], four.Prop("bookings.4.id")!.GetValue<Guid>());
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
