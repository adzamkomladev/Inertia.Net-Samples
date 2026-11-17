using System.Net;
using System.Text.Json;
using MongoDB.Driver;
using CarParker.Data;
using CarParker.Features.Bookings;
using CarParker.Features.Guard;
using CarParker.Features.Lots;

namespace CarParker.Tests;

/// <summary>Lot-local time, the booking page's bay map for a chosen window, and the cancel refund preview.</summary>
public sealed class LotTimeAndAvailabilityTests(AppFactory app) : IClassFixture<AppFactory>
{
    static readonly TimeZoneInfo NewYork = LotTime.Zone("America/New_York");

    [Fact]
    public void Weekly_new_york_slots_stay_at_nine_local_across_both_daylight_saving_changes()
    {
        var input = new BookingInput { WindowType = "recurring", DaysOfWeek = "0", TimeFrom = "09:00", TimeTo = "11:00", Weeks = 2 };

        // US clocks spring forward on Sun 8 Mar 2026: 09:00 is 14:00Z the Sunday before, 13:00Z on the change day.
        var spring = BookingsService.Materialize(input, new DateTime(2026, 2, 28, 12, 0, 0, DateTimeKind.Utc), NewYork);
        Assert.Equal(
            new[] { (Utc(2026, 3, 1, 14), Utc(2026, 3, 1, 16)), (Utc(2026, 3, 8, 13), Utc(2026, 3, 8, 15)) },
            spring);

        // They fall back on Sun 1 Nov 2026: 09:00 is 13:00Z on 25 Oct, 14:00Z on the change day.
        var fall = BookingsService.Materialize(input, new DateTime(2026, 10, 24, 12, 0, 0, DateTimeKind.Utc), NewYork);
        Assert.Equal(
            new[] { (Utc(2026, 10, 25, 13), Utc(2026, 10, 25, 15)), (Utc(2026, 11, 1, 14), Utc(2026, 11, 1, 16)) },
            fall);
        Assert.All(spring.Concat(fall), slot => Assert.Equal(9, LotTime.ToLocal(slot.Start, NewYork).Hour));
    }

    [Fact]
    public void Single_windows_entered_on_the_lot_clock_convert_to_utc_and_gap_times_move_past_the_gap()
    {
        var now = new DateTime(2026, 11, 17, 12, 0, 0, DateTimeKind.Utc);
        var input = new BookingInput { WindowType = "single", Date = "2026-11-18", TimeFrom = "14:00", TimeTo = "18:00" };
        Assert.Equal(new[] { (Utc(2026, 11, 18, 19), Utc(2026, 11, 18, 23)) }, BookingsService.Materialize(input, now, NewYork));

        // The lot's "today" is its own: at 02:00Z on 18 Nov it is still the 17th in New York, so weekly windows start the 18th.
        var weekly = new BookingInput { WindowType = "recurring", DaysOfWeek = "3", TimeFrom = "08:00", TimeTo = "09:00", Weeks = 1 };
        Assert.Equal(Utc(2026, 11, 18, 13), BookingsService.Materialize(weekly, Utc(2026, 11, 18, 2), NewYork)[0].Start);

        // 02:30 does not exist on 8 Mar 2026 in New York; it books as 03:30 EDT (07:30Z).
        Assert.Equal(new DateTime(2026, 3, 8, 7, 30, 0, DateTimeKind.Utc), LotTime.ToUtc(new DateOnly(2026, 3, 8), new TimeOnly(2, 30), NewYork));
        Assert.Equal("New York time", LotTime.ZoneName("America/New_York"));
        Assert.Equal("UTC", LotTime.ZoneName("UTC"));
    }

    [Fact]
    public void Gap_times_move_by_the_zones_real_gap_and_repeated_hours_resolve_to_standard_time()
    {
        // Lord Howe springs forward by 30 minutes (02:00 to 02:30) on 4 Oct 2026, at +10:30 before and +11:00 after.
        var lordHowe = LotTime.Zone("Australia/Lord_Howe");
        Assert.Equal(new DateTime(2026, 10, 3, 15, 45, 0, DateTimeKind.Utc), LotTime.ToUtc(new DateOnly(2026, 10, 4), new TimeOnly(2, 15), lordHowe));
        Assert.Equal(new TimeOnly(2, 45), TimeOnly.FromDateTime(LotTime.ToLocal(LotTime.ToUtc(new DateOnly(2026, 10, 4), new TimeOnly(2, 15), lordHowe), lordHowe)));

        // 01:30 happens twice in New York on 1 Nov 2026; the later (standard time, 06:30Z) is used on both client and server.
        Assert.Equal(Utc(2026, 11, 1, 6) + TimeSpan.FromMinutes(30), LotTime.ToUtc(new DateOnly(2026, 11, 1), new TimeOnly(1, 30), NewYork));
    }

    [Fact]
    public async Task Bay_map_marks_bays_taken_only_for_overlapping_windows_and_such_bays_stay_bookable_otherwise()
    {
        var (client, _) = await app.SharedUserAsync();
        var (lot, spots) = await app.SeedLotAsync("Window map deck", spotCount: 2);
        var day = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(2));
        var start = day.ToDateTime(new TimeOnly(10, 0), DateTimeKind.Utc);
        using (var booked = await app.BookAsync(client, lot, spots[0], start, start.AddHours(2)))
            Assert.StartsWith("/account/bookings/", booked.Headers.Location!.OriginalString);

        var date = day.ToString("yyyy-MM-dd");
        var overlapping = await MapAsync(client, $"/lots/{lot.Id}/book?windowType=single&date={date}&timeFrom=11:00&timeTo=13:00");
        Assert.Equal("booked", overlapping.States[spots[0].Id]);
        Assert.Equal("free", overlapping.States[spots[1].Id]);
        Assert.Equal($"single|{date}|11:00|13:00", overlapping.Window);

        var later = await MapAsync(client, $"/lots/{lot.Id}/book?windowType=single&date={date}&timeFrom=12:00&timeTo=14:00");
        Assert.Equal("free", later.States[spots[0].Id]); // back-to-back is not an overlap

        // Without a valid window the map shows the bays as they are now, and says so.
        var now = await MapAsync(client, $"/lots/{lot.Id}/book");
        Assert.Equal("booked", now.States[spots[0].Id]);
        Assert.Null(now.Window);

        // The bay already holding a booking can be reserved for the free window; the overlapping one is still refused.
        using (var second = await app.BookAsync(client, lot, spots[0], start.AddHours(2), start.AddHours(4)))
            Assert.StartsWith("/account/bookings/", second.Headers.Location!.OriginalString);
        using (var clash = await app.BookAsync(client, lot, spots[0], start.AddHours(1), start.AddHours(3)))
            Assert.False(clash.Headers.Location?.OriginalString.StartsWith("/account/bookings/"));
        Assert.Equal(2, await app.QueryAsync(async db => await db.Reservations.CountDocumentsAsync(r => r.SpotId == spots[0].Id && r.Status == ReservationStatus.Confirmed)));
    }

    [Fact]
    public async Task Cancelling_one_of_two_bookings_on_a_bay_keeps_it_held_for_the_other()
    {
        var (client, _) = await app.SharedUserAsync();
        var (lot, spots) = await app.SeedLotAsync("Shared bay deck", spotCount: 1);
        var start = DateTime.UtcNow.AddDays(3);
        using var first = await app.BookAsync(client, lot, spots[0], start, start.AddHours(1));
        using var second = await app.BookAsync(client, lot, spots[0], start.AddHours(2), start.AddHours(3));
        using (await app.CancelBookingAsync(client, AppFactory.BookingIdOf(first))) { }
        Assert.Equal(SpotState.Booked, await app.QueryAsync(async db => await db.Spots.Find(s => s.Id == spots[0].Id).Project(s => s.State).SingleAsync()));
        using (await app.CancelBookingAsync(client, AppFactory.BookingIdOf(second))) { }
        Assert.Equal(SpotState.Free, await app.QueryAsync(async db => await db.Spots.Find(s => s.Id == spots[0].Id).Project(s => s.State).SingleAsync()));
    }

    [Fact]
    public async Task Cancelling_ignores_a_session_open_on_the_bay_for_another_booking()
    {
        var (client, _) = await app.SharedUserAsync();
        var (guard, _) = await app.SharedUserAsync(UserRole.Guard);
        var (lot, spots) = await app.SeedLotAsync("Scoped cancel deck", spotCount: 1);
        var now = DateTime.UtcNow;
        using var current = await app.BookAsync(client, lot, spots[0], now.AddMinutes(5), now.AddMinutes(65));
        using var later = await app.BookAsync(client, lot, spots[0], now.AddHours(5), now.AddHours(6));
        using (await app.SendAsync(guard, "/guard/check-in", new { lotId = lot.Id, spotId = spots[0].Id })) { }
        Assert.Equal(1, await app.QueryAsync(async db => await db.ParkingSessions.CountDocumentsAsync(s => s.SpotId == spots[0].Id && s.Status == SessionStatus.InProgress)));

        // The later booking is not the one parked, so it can be cancelled; the parked one cannot.
        using (await app.CancelBookingAsync(client, AppFactory.BookingIdOf(later))) { }
        Assert.Equal(ReservationStatus.Cancelled, (await app.ReservationAsync(AppFactory.BookingIdOf(later)))!.Status);
        using (await app.CancelBookingAsync(client, AppFactory.BookingIdOf(current))) { }
        Assert.Equal(ReservationStatus.Confirmed, (await app.ReservationAsync(AppFactory.BookingIdOf(current)))!.Status);
    }

    [Fact]
    public async Task Check_in_and_checkout_cope_with_two_bookings_on_one_bay()
    {
        var (client, _) = await app.SharedUserAsync();
        var (guard, _) = await app.SharedUserAsync(UserRole.Guard);
        var (lot, spots) = await app.SeedLotAsync("Two window deck", spotCount: 1);
        var now = DateTime.UtcNow;
        using var first = await app.BookAsync(client, lot, spots[0], now.AddMinutes(5), now.AddMinutes(65));
        using var second = await app.BookAsync(client, lot, spots[0], now.AddHours(3), now.AddHours(4));
        var spotId = spots[0].Id;
        async Task<SpotState> StateAsync() => await app.QueryAsync(async db => await db.Spots.Find(s => s.Id == spotId).Project(s => s.State).SingleAsync());

        using (await app.SendAsync(guard, "/guard/check-in", new { lotId = lot.Id, spotId })) { }
        Assert.Equal(SpotState.Occupied, await StateAsync());
        // Checkout hands the bay back to the booking still ahead, not to Free.
        using (await app.SendAsync(guard, "/guard/check-out", new { lotId = lot.Id, spotId })) { }
        Assert.Equal(SpotState.Booked, await StateAsync());

        // A bay left Free (e.g. an older checkout) still checks in a confirmed window that is current.
        await app.QueryAsync(async db =>
        {
            await db.Spots.UpdateOneAsync(s => s.Id == spotId, Builders<Spot>.Update.Set(s => s.State, SpotState.Free));
            await db.Occurrences.UpdateOneAsync(o => o.ReservationId == AppFactory.BookingIdOf(second),
                Builders<Occurrence>.Update.Set(o => o.StartAtUtc, now.AddMinutes(2)).Set(o => o.EndAtUtc, now.AddMinutes(60)));
            return true;
        });
        using (await app.SendAsync(guard, "/guard/check-in", new { lotId = lot.Id, spotId })) { }
        Assert.Equal(SpotState.Occupied, await StateAsync());
        Assert.Equal(2, await app.QueryAsync(async db => await db.ParkingSessions.CountDocumentsAsync(s => s.SpotId == spotId)));
    }

    [Fact]
    public async Task A_booking_that_stalls_before_commit_cannot_double_book_a_bay_another_booking_took()
    {
        if (MongoModeConfig.Parse(AppFactory.TestMode) == MongoMode.Single) return; // single mode has no transactions to conflict on

        var (rival, rivalId) = await app.SharedUserAsync();
        var late = rival;
        var (lot, spots) = await app.SeedLotAsync("Stalled booking deck", spotCount: 1);
        var spotId = spots[0].Id;
        var start = DateTime.UtcNow.AddHours(4);
        var stalledAtWrite = new TaskCompletionSource();
        var resume = new TaskCompletionSource();

        // Request A passed its overlap check and then stalls inside its transaction, after its first write to the bay.
        var stalled = app.QueryAsync(async db =>
        {
            await db.TransactionAsync(async session =>
            {
                await db.Spots.UpdateOneAsync(session, s => s.Id == spotId, Builders<Spot>.Update.Inc(s => s.BookingVersion, 1));
                stalledAtWrite.TrySetResult();
                await resume.Task;
                var reservation = new Reservation { UserId = rivalId, LotId = lot.Id, SpotId = spotId, StartAtUtc = start, EndAtUtc = start.AddHours(2), OccurrenceCount = 1 };
                await db.Reservations.InsertOneAsync(session, reservation);
                await db.Occurrences.InsertOneAsync(session, new Occurrence { ReservationId = reservation.Id, StartAtUtc = start, EndAtUtc = start.AddHours(2) });
            });
            return true;
        });
        await stalledAtWrite.Task;

        // Request B arrives for the same window while A is mid-flight; its transaction conflicts with A's on the bay.
        var other = app.BookAsync(late, lot, spots[0], start, start.AddHours(2));
        await Task.Delay(1500);
        Assert.False(other.IsCompleted);
        resume.SetResult();
        await stalled;
        using var response = await other;

        Assert.False(response.Headers.Location?.OriginalString.StartsWith("/account/bookings/"));
        Assert.Equal(1, await app.QueryAsync(async db => await db.Reservations.CountDocumentsAsync(r => r.SpotId == spotId && r.Status == ReservationStatus.Confirmed)));
    }

    [Theory]
    [InlineData(30, true, "24h_full", 800)]
    [InlineData(10, true, "4h_half", 400)]
    [InlineData(2, true, "late_none", 0)]
    [InlineData(30, false, "24h_full", 0)]
    public async Task Booking_page_previews_the_refund_cancelling_now_would_request(int hoursOut, bool pay, string rule, long cents)
    {
        var (client, _) = await app.SharedUserAsync();
        var (lot, spots) = await app.SeedLotAsync($"Preview deck {hoursOut}{pay}", spotCount: 1);
        var start = DateTime.UtcNow.AddHours(hoursOut);
        using var created = await app.BookAsync(client, lot, spots[0], start, start.AddHours(2));
        var id = AppFactory.BookingIdOf(created);
        if (pay) using (await app.PayBookingAsync(client, id)) { }

        using var page = await app.InertiaGetAsync(client, $"/account/bookings/{id}");
        using var json = JsonDocument.Parse(await page.Content.ReadAsStringAsync());
        var props = json.RootElement.GetProperty("props");
        var preview = props.GetProperty("cancelPreview");
        Assert.Equal(rule, preview.GetProperty("rule").GetString());
        Assert.Equal(cents, preview.GetProperty("amountCents").GetInt64());
        Assert.Equal(pay, preview.GetProperty("paid").GetBoolean());
        Assert.Equal(LotTime.DefaultZoneId, props.GetProperty("booking").GetProperty("lotTimeZoneId").GetString());

        // The preview matches what cancelling then requests.
        using (await app.CancelBookingAsync(client, id)) { }
        var refund = await app.QueryAsync(async db => await db.Refunds.Find(r => r.ReservationId == id).FirstOrDefaultAsync());
        Assert.Equal(cents, refund?.AmountCents ?? 0);
    }

    async Task<(Dictionary<Guid, string> States, string? Window)> MapAsync(HttpClient client, string url)
    {
        using var page = await app.InertiaGetAsync(client, url);
        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        using var json = JsonDocument.Parse(await page.Content.ReadAsStringAsync());
        var props = json.RootElement.GetProperty("props");
        var states = props.GetProperty("spots").EnumerateArray()
            .ToDictionary(s => s.GetProperty("id").GetGuid(), s => s.GetProperty("state").GetString()!);
        var window = props.TryGetProperty("spotsWindow", out var echo) && echo.ValueKind == JsonValueKind.String ? echo.GetString() : null;
        return (states, window);
    }

    static DateTime Utc(int year, int month, int day, int hour) => new(year, month, day, hour, 0, 0, DateTimeKind.Utc);
}
