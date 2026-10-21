using Bogus;
using Microsoft.AspNetCore.Identity;
using MongoDB.Driver;
using SvelteMongoBlog.Features.Admin;
using SvelteMongoBlog.Features.Alerts;
using SvelteMongoBlog.Features.Bookings;
using SvelteMongoBlog.Features.Guard;
using SvelteMongoBlog.Features.Incidents;
using SvelteMongoBlog.Features.Insurance;
using SvelteMongoBlog.Features.Lots;

namespace SvelteMongoBlog.Data;

/// <summary>Repeatable CarParker demo seed (Bogus seed 42): 1 admin + 2 guards + 3 customers,
/// 5 lots with A–C section grids, 30 reservations (recurring/live/past/cancelled/no-show), guard sessions,
/// payments, requested refunds, Spot Cover claims, an incident and alerts. Every phase checkpoints only
/// after its transaction commits and re-runs wipe + rebuild its own documents first, so resume heals
/// any phase that was interrupted mid-write. Demo credentials live in README.md.</summary>
public static class DemoSeeder
{
    public const string Password = "CarParker-42-demo!";

    static readonly (string Email, string Name, UserRole Role, string Phone, string Key)[] Users =
    [
        ("admin@carparker.test", "Allan Park", UserRole.Admin, "+1 555 010 0200", "demo-42-user-admin"),
        ("guard@carparker.test", "Gus Harmon", UserRole.Guard, "+1 555 010 0201", "demo-42-user-guard"),
        ("guard2@carparker.test", "Iris Chen", UserRole.Guard, "+1 555 010 0202", "demo-42-user-guard2"),
        ("customer@carparker.test", "Casey Reed", UserRole.Customer, "+1 555 010 0203", "demo-42-user-customer"),
        ("customer2@carparker.test", "Noor Haddad", UserRole.Customer, "+1 555 010 0204", "demo-42-user-customer2"),
        ("customer3@carparker.test", "Devon Marsh", UserRole.Customer, "+1 555 010 0205", "demo-42-user-customer3"),
    ];

    static readonly (string Key, string Name, string Address, string Area, double Lat, double Lng, int LevelCount, long Rate, long DailyMax, long Coverage, string Phone)[] Lots =
    [
        ("demo-42-lot-central", "Central Gateway Garage", "1 Gateway Plaza", "City Center", 40.7580, -73.9855, 3, 600, 3200, 75_000, "+1 (555) 010-2031"),
        ("demo-42-lot-riverside", "Riverside Deck", "48 Boardwalk Ave", "Riverside", 40.7530, -73.9893, 2, 350, 1900, 50_000, "+1 (555) 010-2032"),
        ("demo-42-lot-mall", "Mall Point Parkade", "7 Mall Point Rd", "Market District", 40.7440, -73.9871, 2, 250, 1600, 50_000, "+1 (555) 010-2033"),
        ("demo-42-lot-stadium", "Stadium Underground", "90 Stadium Loop", "Fairgrounds", 40.7505, -73.9934, 2, 300, 1200, 40_000, "+1 (555) 010-2034"),
        ("demo-42-lot-airport", "Airport Long Stay B", "200 Airfield Way", "Airport", 40.7560, -73.9860, 3, 180, 750, 60_000, "+1 (555) 010-2035"),
    ];

    public static async Task RunAsync(AppDb db, CancellationToken ct = default)
    {
        var anchor = await db.SeedCheckpoints.Find(x => x.Name == "demo-42-anchor").FirstOrDefaultAsync(ct);
        if (anchor is null)
        {
            anchor = new SeedCheckpoint { Name = "demo-42-anchor", Seed = 42, UpdatedAt = DateTime.UtcNow };
            await db.SeedCheckpoints.InsertOneAsync(anchor, cancellationToken: ct);
        }
        var now = DateTime.SpecifyKind(anchor.UpdatedAt, DateTimeKind.Utc);
        var passwordHash = new PasswordHasher<User>().HashPassword(new User { Email = "", Name = "" }, Password);

        await PhaseAsync(db, "users", async session =>
        {
            await db.Users.DeleteManyAsync(session, u => u.DemoKey != null && u.DemoKey.StartsWith("demo-42-"), cancellationToken: ct);
            await db.Users.InsertManyAsync(session, Users.Select(u => new User
            {
                DemoKey = u.Key, Email = u.Email, Name = u.Name, PasswordHash = passwordHash, Phone = u.Phone,
                Role = u.Role, EmailVerifiedAt = now.AddDays(-120), IsDemo = true,
                CreatedAt = now.AddDays(-120), Preferences = new UserPreferences { Theme = "system" },
            }).ToArray(), cancellationToken: ct);
        }, ct);

        await PhaseAsync(db, "lots", async session =>
        {
            await db.Lots.DeleteManyAsync(session, l => l.DemoKey != null && l.DemoKey.StartsWith("demo-42-"), cancellationToken: ct);
            await db.Lots.InsertManyAsync(session, Lots.Select(l => new Lot
            {
                DemoKey = l.Key, Name = l.Name, Address = l.Address, Area = l.Area, Lat = l.Lat, Lng = l.Lng,
                LevelCount = l.LevelCount, RatePerHourCents = l.Rate, DailyMaxCents = l.DailyMax,
                ContactPhone = l.Phone, InsuranceCovered = true, CoverageCents = l.Coverage,
                CreatedAt = now.AddDays(-90), UpdatedAt = now.AddDays(-90), IndexVersion = 1,
            }).ToArray(), cancellationToken: ct);
        }, ct);

        await PhaseAsync(db, "spots", async session =>
        {
            var demoLots = await DemoLotsAsync(db, ct);
            var lotIds = demoLots.Select(l => l.Id).ToArray();
            // Re-create the grid for every lot. Demo spot ids may change per resume, which is fine
            // because the booking phase resolves demo spots fresh by lot set.
            await db.Spots.DeleteManyAsync(session, s => lotIds.Contains(s.LotId), cancellationToken: ct);
            foreach (var lot in demoLots)
                await Admin.SeedSpotsAsync(db, lot.Id, lot.LevelCount, ct);
        }, ct);

        await PhaseAsync(db, "bookings", async session =>
        {
            var demoUsers = await db.Users.Find(u => u.DemoKey != null && u.DemoKey.StartsWith("demo-42-user-")).ToListAsync(ct);
            var demoLots = await DemoLotsAsync(db, ct);
            var lotIds = demoLots.Select(l => l.Id).ToArray();
            var demoSpots = await db.Spots.Find(s => lotIds.Contains(s.LotId))
                .SortBy(s => s.LotId).ThenBy(s => s.Level).ThenBy(s => s.Label).ToListAsync(ct);
            if (demoUsers.Count != Users.Length || demoLots.Count != Lots.Length || demoSpots.Count == 0)
                throw new InvalidOperationException("Demo users/lots/spots are incomplete; clear the demo checkpoint rows and run --seed-demo again.");

            var adminId = demoUsers.First(u => u.Role == UserRole.Admin).Id;
            var guardIds = demoUsers.Where(u => u.Role == UserRole.Guard).Select(u => u.Id).OrderBy(x => x).ToArray();
            var customers = demoUsers.Where(u => u.Role == UserRole.Customer).OrderBy(u => u.Email).ToArray();
            var faker = new Faker { Random = new Randomizer(42) };

            // Reset the grid to free, then repaint occupancy states derived from the bookings below.
            await db.Spots.UpdateManyAsync(session, s => lotIds.Contains(s.LotId),
                Builders<Spot>.Update.Set(s => s.State, SpotState.Free)
                    .Unset(s => s.LastCheckpointAt).Unset(s => s.CheckpointNote), cancellationToken: ct);

            // Wipe this phase's documents first so unique indexes hold across a resumed run.
            var spotIds = demoSpots.Select(s => s.Id).ToArray();
            var oldReservations = await db.Reservations.Find(r => spotIds.Contains(r.SpotId)).Project(r => r.Id).ToListAsync(ct);
            if (oldReservations.Count > 0)
            {
                var oldPolicies = await db.SpotCoverPolicies.Find(p => oldReservations.Contains(p.ReservationId)).Project(p => p.Id).ToListAsync(ct);
                await db.Occurrences.DeleteManyAsync(session, o => oldReservations.Contains(o.ReservationId), cancellationToken: ct);
                await db.Payments.DeleteManyAsync(session, p => oldReservations.Contains(p.ReservationId), cancellationToken: ct);
                await db.Refunds.DeleteManyAsync(session, r => oldReservations.Contains(r.ReservationId), cancellationToken: ct);
                await db.SpotCoverPolicies.DeleteManyAsync(session, p => oldReservations.Contains(p.ReservationId), cancellationToken: ct);
                if (oldPolicies.Count > 0) await db.PolicyClaims.DeleteManyAsync(session, c => oldPolicies.Contains(c.PolicyId), cancellationToken: ct);
                await db.ParkingSessions.DeleteManyAsync(session, x => spotIds.Contains(x.SpotId), cancellationToken: ct);
                await db.Reservations.DeleteManyAsync(session, r => oldReservations.Contains(r.Id), cancellationToken: ct);
            }
            await db.SecurityIncidents.DeleteManyAsync(session, x => lotIds.Contains(x.LotId), cancellationToken: ct);
            var demoUserIds = demoUsers.Select(u => u.Id).ToArray();
            await db.Alerts.DeleteManyAsync(session, a => demoUserIds.Contains(a.UserId), cancellationToken: ct);

            var reservations = new List<Reservation>();
            var occurrences = new List<Occurrence>();
            var payments = new List<Payment>();
            var policies = new List<SpotCoverPolicy>();
            var claims = new List<PolicyClaim>();
            var refunds = new List<Refund>();
            var sessions = new List<ParkingSession>();
            var alerts = new List<Alert>();
            var incidents = new List<SecurityIncident>();
            var spotStates = new Dictionary<Guid, SpotState>();

            void SetSpot(Spot spot, SpotState state) => spotStates[spot.Id] = state;
            void AddAlert(User user, string kind, string title, string body, string href, bool unread) =>
                alerts.Add(new Alert { UserId = user.Id, Kind = kind, Title = title, Body = body, Href = href, At = now.AddHours(-1), ReadAt = unread ? null : now.AddHours(-1) });
            void AddExitedSession(Reservation reservation, Guid guardId, DateTime enteredAt, DateTime exitedAt)
            {
                var occurrence = occurrences.Last(o => o.ReservationId == reservation.Id);
                sessions.Add(new ParkingSession
                {
                    OccurrenceId = occurrence.Id, SpotId = reservation.SpotId, LotId = reservation.LotId,
                    PlateNo = reservation.PlateNo!, EnteredAtUtc = enteredAt, ExitedAtUtc = exitedAt,
                    EnteredById = guardId, ExitedById = guardId,
                    Status = SessionStatus.Exited, CreatedAt = enteredAt,
                });
            }

            // Ten recipes per customer → 30 reservations covering recurring/live/past/cancelled/no-show.
            // Each reservation takes a distinct spot (7 is coprime with the spot count), so demo windows
            // can never overlap and grid/booking state stays consistent.
            for (var k = 0; k < customers.Length; k++)
            {
                var user = customers[k];

                for (var i = 0; i < 10; i++)
                {
                    var lot = demoLots[(k + i * 2) % demoLots.Count];
                    var spot = demoSpots[(k * 13 + i * 7 + 3) % demoSpots.Count];
                    var plate = $"CP-{k}{i:00}";
                    var createdAt = now.AddDays(-faker.Random.Int(3, 20)).AddHours(-faker.Random.Int(0, 20));

                    switch (i)
                    {
                        // i0: live recurring — past weeks are done, the rest stays scheduled.
                        case 0:
                        {
                            var slots = WeeklySlots(now.AddDays(-14).AddHours(9), 28, [1, 5], "09:00", "11:00");
                            var reservation = AddReservation(reservations, occurrences, payments, policies, user, lot, spot,
                                plate, createdAt, WindowType.Recurring, slots, PaymentMethod.Card, paid: true, cover: true,
                                pattern: new RecurrencePattern([1, 5], "09:00", "11:00", Weeks: 4),
                                classify: s => s.End < now ? OccurrenceStatus.Done : OccurrenceStatus.Scheduled);
                            SetSpot(spot, SpotState.Booked);
                            break;
                        }
                        // i1: upcoming recurring — fully scheduled from tomorrow, pay-at-gate booking.
                        case 1:
                        {
                            var slots = WeeklySlots(now.AddDays(1).AddHours(17), 28, [2, 4], "17:00", "19:00");
                            var reservation = AddReservation(reservations, occurrences, payments, policies, user, lot, spot,
                                plate, createdAt, WindowType.Recurring, slots, PaymentMethod.PayAtGate, paid: false,
                                pattern: new RecurrencePattern([2, 4], "17:00", "19:00", Weeks: 4));
                            SetSpot(spot, SpotState.Booked);
                            break;
                        }
                        // i2/i3: past singles with exited guard-logged sessions (reservation completed).
                        case 2 or 3:
                        {
                            var daysAgo = i == 2 ? 3 : 8;
                            var slot = (now.AddDays(-daysAgo).AddHours(9 + k), now.AddDays(-daysAgo).AddHours(11 + k));
                            var reservation = AddReservation(reservations, occurrences, payments, policies, user, lot, spot,
                                plate, createdAt, WindowType.Single, [slot], PaymentMethod.Card, paid: true, complete: true);
                            AddExitedSession(reservation, guardIds[k % guardIds.Length], slot.Item1.AddMinutes(2), slot.Item2.AddMinutes(-3));
                            AddAlert(user, "session", "Parking session ended", $"Spot {spot.Label} is free again.",
                                $"/account/bookings/{reservation.Id}", unread: false);
                            if (faker.Random.Bool(0.34f)) AddPolicy(policies, reservation, lot, expired: true);
                            break;
                        }
                        // i4: live checked-in session; customer 1's window already ended → overstay demo.
                        case 4:
                        {
                            var from = k == 0 ? now.AddMinutes(-270) : now.AddMinutes(-150);
                            var to = k == 0 ? now.AddMinutes(-60) : now.AddMinutes(60);
                            var reservation = AddReservation(reservations, occurrences, payments, policies, user, lot, spot,
                                plate, createdAt, WindowType.Single, new[] { (from, to) }, PaymentMethod.Card, paid: true,
                                cover: true, classify: _ => OccurrenceStatus.Active);
                            sessions.Add(new ParkingSession
                            {
                                OccurrenceId = occurrences.Last(o => o.ReservationId == reservation.Id).Id,
                                SpotId = reservation.SpotId, LotId = reservation.LotId, PlateNo = plate,
                                EnteredAtUtc = from.AddMinutes(7), EnteredById = guardIds[k % guardIds.Length],
                                Status = SessionStatus.InProgress, CreatedAt = from.AddMinutes(7),
                            });
                            SetSpot(spot, SpotState.Occupied);
                            break;
                        }
                        // i5/i6: future singles (card / pay at gate).
                        case 5 or 6:
                        {
                            var offset = i == 5 ? 2 + k : 5 + k;
                            var gate = i == 6;
                            var reservation = AddReservation(reservations, occurrences, payments, policies, user, lot, spot,
                                plate, createdAt, WindowType.Single,
                                new[] { (now.AddDays(offset).AddHours(9), now.AddDays(offset).AddHours(12)) },
                                gate ? PaymentMethod.PayAtGate : PaymentMethod.Card, paid: !gate, cover: !gate);
                            SetSpot(spot, SpotState.Booked);
                            break;
                        }
                        // i7: cancelled with an automatic refund request per the rule matrix (full ×2 + half ×1).
                        case 7:
                        {
                            var (hoursOut, cancelledHoursAgo) = k switch { 0 => (36, 4), 1 => (10, 2), _ => (40, 5) };
                            var start = now.AddHours(hoursOut);
                            var cancelledAt = now.AddHours(-cancelledHoursAgo);
                            var reservation = AddReservation(reservations, occurrences, payments, policies, user, lot, spot,
                                plate, cancelledAt.AddDays(-3), WindowType.Single,
                                new[] { (start, start.AddHours(2)) }, PaymentMethod.Card, paid: true, cover: true);
                            reservation.Status = ReservationStatus.Cancelled;
                            reservation.CancelledAt = cancelledAt;
                            foreach (var occurrence in occurrences.Where(o => o.ReservationId == reservation.Id))
                                occurrence.Status = OccurrenceStatus.Cancelled;
                            var (rule, amount) = RefundRules.ComputeCents(start, reservation.TotalCents, cancelledAt);
                            refunds.Add(new Refund
                            {
                                ReservationId = reservation.Id,
                                PaymentId = payments.Last(p => p.ReservationId == reservation.Id).Id,
                                RequestedByUserId = user.Id, Rule = rule, AmountCents = amount,
                                Reason = $"Cancelled {hoursOut - cancelledHoursAgo}h before the window",
                                CreatedAt = cancelledAt,
                            });
                            AddAlert(user, "refund", "Refund requested", "Your cancellation is awaiting review.", "/account/payments", unread: true);
                            break;
                        }
                        // i8: per-customer flavour — second live session / no-show / far-out gate booking.
                        case 8 when k == 0:
                        {
                            var from = now.AddMinutes(-100);
                            var reservation = AddReservation(reservations, occurrences, payments, policies, user, lot, spot,
                                plate, createdAt, WindowType.Single, new[] { (from, now.AddMinutes(40)) },
                                PaymentMethod.Card, paid: true, classify: _ => OccurrenceStatus.Active);
                            sessions.Add(new ParkingSession
                            {
                                OccurrenceId = occurrences.Last(o => o.ReservationId == reservation.Id).Id,
                                SpotId = reservation.SpotId, LotId = reservation.LotId, PlateNo = plate,
                                EnteredAtUtc = now.AddMinutes(-95), EnteredById = guardIds[1 % guardIds.Length],
                                Status = SessionStatus.InProgress, CreatedAt = now.AddMinutes(-95),
                            });
                            SetSpot(spot, SpotState.Occupied);
                            break;
                        }
                        case 8 when k == 1:
                        {
                            var reservation = AddReservation(reservations, occurrences, payments, policies, user, lot, spot,
                                plate, now.AddDays(-3), WindowType.Single,
                                new[] { (now.AddHours(-50), now.AddHours(-48)) }, PaymentMethod.Card, paid: true);
                            // The sweep already scored this window: occurrence no_show, spot released, no refund (late_none).
                            occurrences.Last(o => o.ReservationId == reservation.Id).Status = OccurrenceStatus.NoShow;
                            AddAlert(user, "gate", "No-show recorded",
                                "A spot window was released after 30 minutes without a check-in.", "/account/bookings", unread: true);
                            break;
                        }
                        case 8:
                        {
                            var reservation = AddReservation(reservations, occurrences, payments, policies, user, lot, spot,
                                plate, createdAt, WindowType.Single,
                                new[] { (now.AddDays(12).AddHours(8), now.AddDays(12).AddHours(10)) },
                                PaymentMethod.PayAtGate, paid: false);
                            SetSpot(spot, SpotState.Booked);
                            break;
                        }
                        // i9: fully completed past recurring — every week drove, checked out, reservation completed.
                        default:
                        {
                            var slots = WeeklySlots(now.AddDays(-16).AddHours(12), 14, [3, 5], "12:00", "14:00");
                            var reservation = AddReservation(reservations, occurrences, payments, policies, user, lot, spot,
                                plate, createdAt, WindowType.Recurring, slots, PaymentMethod.Card, paid: true, cover: true,
                                pattern: new RecurrencePattern([3, 5], "12:00", "14:00", Weeks: 2), complete: true);
                            AddExitedSession(reservation, guardIds[k % guardIds.Length],
                                slots[^1].Start.AddMinutes(2), slots[^1].End.AddMinutes(-3));
                            AddAlert(user, "session", "Parking session ended", $"Spot {spot.Label} is free again.",
                                $"/account/bookings/{reservation.Id}", unread: false);
                            break;
                        }
                    }
                }
            }

            // Two Spot Cover claims for the admin queue: one pending, one already paid.
            var policy0 = policies.First(p => p.Status == PolicyStatus.Active);
            var policy1 = policies.First(p => p.UserId == customers[1].Id && p.Status == PolicyStatus.Active);
            claims.Add(new PolicyClaim
            {
                PolicyId = policy0.Id, UserId = policy0.UserId, Kind = ClaimKind.Damage,
                Description = "Dented fender while turning into a bay: scrape along the left side.",
                IncidentOn = now.AddHours(-20), ClaimedCents = Math.Min(40_000, policy0.CoverageCents),
                Status = ClaimStatus.Submitted, CreatedAt = now.AddHours(-18),
            });
            claims.Add(new PolicyClaim
            {
                PolicyId = policy1.Id, UserId = policy1.UserId, Kind = ClaimKind.Theft,
                Description = "Hub caps and a dash camera were missing after an overnight stay.",
                IncidentOn = now.AddDays(-4), ClaimedCents = 25_000, PayoutCents = 25_000,
                Status = ClaimStatus.Paid, DecidedByUserId = adminId,
                DecisionNote = "Receipts verified; paid out after inspection.",
                CreatedAt = now.AddDays(-6), DecidedAt = now.AddHours(-30), PaidAt = now.AddHours(-29),
            });
            AddAlert(customers[0], "policy", "Claim submitted", "We received your damage claim.", "/account/insurance", unread: false);
            AddAlert(customers[1], "policy", "Claim paid", "Your theft claim was approved and paid.", "/account/insurance", unread: false);

            // One open incident for the guard console.
            incidents.Add(new SecurityIncident
            {
                LotId = demoLots[0].Id, ReporterUserId = guardIds[0], Kind = "theft",
                Description = "Spare parts reported missing from a blue hatchback parked near the B section overnight.",
                Status = IncidentStatus.Open, CreatedAt = now.AddHours(-6),
            });

            await db.Reservations.InsertManyAsync(session, reservations, cancellationToken: ct);
            await db.Occurrences.InsertManyAsync(session, occurrences, cancellationToken: ct);
            await db.Payments.InsertManyAsync(session, payments, cancellationToken: ct);
            await db.Refunds.InsertManyAsync(session, refunds, cancellationToken: ct);
            await db.SpotCoverPolicies.InsertManyAsync(session, policies, cancellationToken: ct);
            await db.PolicyClaims.InsertManyAsync(session, claims, cancellationToken: ct);
            await db.ParkingSessions.InsertManyAsync(session, sessions, cancellationToken: ct);
            await db.Alerts.InsertManyAsync(session, alerts, cancellationToken: ct);
            await db.SecurityIncidents.InsertManyAsync(session, incidents, cancellationToken: ct);

            // Repaint spots that must hold a live demo state (booked/occupied) and block one or two bays.
            foreach (var pair in spotStates)
                await db.Spots.UpdateOneAsync(session, s => s.Id == pair.Key,
                    Builders<Spot>.Update.Set(s => s.State, pair.Value), cancellationToken: ct);
            var blockNotes = new[] { "Blocked for repainting — manual override demo.", "Charging cabinet window under maintenance." };
            for (var lotIndex = 0; lotIndex < demoLots.Count; lotIndex++)
                for (var b = 0; b <= lotIndex % 2; b++)
                    await db.Spots.UpdateOneAsync(session, s => s.LotId == demoLots[lotIndex].Id && s.Label == $"A-{(b == 0 ? "06" : "02")}",
                        Builders<Spot>.Update.Set(s => s.State, SpotState.Blocked)
                            .Set(s => s.CheckpointNote, blockNotes[b % blockNotes.Length])
                            .Set(s => s.LastCheckpointAt, now), cancellationToken: ct);
        }, ct);

        Console.WriteLine($"Demo seed complete: {Users.Length} staff/customers, {Lots.Length} lots, 30 reservations with sessions, payments, refunds, claims, an incident and alerts.");
    }

    // ---------- phase checkpoint machinery ----------

    static async Task PhaseAsync(AppDb db, string name, Func<IClientSessionHandle, Task> write, CancellationToken ct)
    {
        var key = "demo-42-" + name;
        var checkpoint = await db.SeedCheckpoints.Find(x => x.Name == key).FirstOrDefaultAsync(ct);
        if (checkpoint is { Completed: > 0 }) return; // resumable: the phase already committed
        await db.TransactionAsync(async session =>
        {
            // Writes and the progress marker share a transaction; single mode runs best effort like the app.
            await write(session);
            await db.SeedCheckpoints.UpdateOneAsync(session, x => x.Name == key,
                Builders<SeedCheckpoint>.Update.SetOnInsert(x => x.Name, key).SetOnInsert(x => x.Seed, 42)
                    .Set(x => x.Completed, 1).Set(x => x.UpdatedAt, DateTime.UtcNow),
                new UpdateOptions { IsUpsert = true }, ct);
        }, ct);
        Console.WriteLine($"Seed {name}: done");
    }

    static async Task<List<Lot>> DemoLotsAsync(AppDb db, CancellationToken ct) =>
        await db.Lots.Find(l => l.DemoKey != null && l.DemoKey.StartsWith("demo-42-lot-")).ToListAsync(ct);

    // ---------- composition helpers ----------

    /// <summary>Builds the reservation, its occurrences and its payment; attaches an Active Spot Cover
    /// policy when `cover` (premium billed at booking, included in TotalCents).</summary>
    static Reservation AddReservation(List<Reservation> reservations, List<Occurrence> occurrences,
        List<Payment> payments, List<SpotCoverPolicy> policies, User user, Lot lot, Spot spot, string plate,
        DateTime createdAt, WindowType windowType, IReadOnlyList<(DateTime Start, DateTime End)> slots,
        PaymentMethod method, bool paid, RecurrencePattern? pattern = null,
        Func<(DateTime Start, DateTime End), OccurrenceStatus>? classify = null, bool cover = false, bool complete = false)
    {
        var reservation = new Reservation
        {
            UserId = user.Id, LotId = lot.Id, SpotId = spot.Id, WindowType = windowType, Pattern = pattern,
            OccurrenceCount = slots.Count, PlateNo = plate, CreatedAt = createdAt,
            ParkingCents = slots.Sum(s => PricingRules.WindowCents(lot.RatePerHourCents, lot.DailyMaxCents, s.Start, s.End)),
        };
        if (windowType == WindowType.Single)
        {
            reservation.StartAtUtc = slots[0].Start;
            reservation.EndAtUtc = slots[0].End;
        }
        if (cover)
        {
            reservation.InsuranceCents = PricingRules.InsurancePremiumCents(reservation.ParkingCents);
            reservation.TotalCents = reservation.ParkingCents + reservation.InsuranceCents;
        }
        else reservation.TotalCents = reservation.ParkingCents;
        reservation.Status = ReservationStatus.Confirmed;

        for (var index = 0; index < slots.Count; index++)
        {
            var status = classify?.Invoke(slots[index]) ?? (complete ? OccurrenceStatus.Done : OccurrenceStatus.Scheduled);
            occurrences.Add(new Occurrence
            {
                ReservationId = reservation.Id, Index = index, StartAtUtc = slots[index].Start,
                EndAtUtc = slots[index].End, Status = status, CreatedAt = createdAt,
            });
        }
        if (complete)
        {
            reservation.Status = ReservationStatus.Completed;
            reservation.CompletedAt = slots.Max(s => s.End).AddMinutes(-3);
        }
        payments.Add(new Payment
        {
            ReservationId = reservation.Id, UserId = user.Id, Method = method, AmountCents = reservation.TotalCents,
            Status = paid ? PaymentStatus.Paid : PaymentStatus.Pending,
            TxRef = paid ? $"pay_{reservation.Id:N}".AsSpan(0, 24).ToString() : null,
            PaidAt = paid ? createdAt.AddMinutes(5) : null, CreatedAt = createdAt,
        });
        if (cover) AddPolicy(policies, reservation, lot);
        return reservation;
    }

    static void AddPolicy(List<SpotCoverPolicy> policies, Reservation reservation, Lot lot, bool expired = false) =>
        policies.Add(new SpotCoverPolicy
        {
            ReservationId = reservation.Id, UserId = reservation.UserId, LotId = lot.Id,
            PremiumCents = PricingRules.InsurancePremiumCents(reservation.ParkingCents),
            CoverageCents = lot.CoverageCents, StartedAt = reservation.CreatedAt,
            Status = expired ? PolicyStatus.Expired : PolicyStatus.Active, CreatedAt = reservation.CreatedAt,
        });

    // Recurring windows that may extend into the past: day offsets from the base date, week-matching days only.
    static List<(DateTime Start, DateTime End)> WeeklySlots(DateTime baseStart, int daySpan, int[] daysOfWeek, string timeFrom, string timeTo)
    {
        TimeOnly.TryParseExact(timeFrom, "HH:mm", out var from);
        TimeOnly.TryParseExact(timeTo, "HH:mm", out var to);
        var slots = new List<(DateTime Start, DateTime End)>();
        for (var offset = 0; offset < daySpan; offset++)
        {
            var day = DateOnly.FromDateTime(baseStart).AddDays(offset);
            if (!daysOfWeek.Contains((int)day.DayOfWeek)) continue;
            slots.Add((day.ToDateTime(from, DateTimeKind.Utc), day.ToDateTime(to, DateTimeKind.Utc)));
        }
        return slots;
    }
}
