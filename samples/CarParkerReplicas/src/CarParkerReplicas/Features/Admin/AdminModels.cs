using MongoDB.Driver;
using CarParker.Data;
using CarParker.Features.Bookings;
using CarParker.Features.Guard;
using CarParker.Features.Insurance;
using CarParker.Features.Lots;
using CarParker.Infrastructure;

namespace CarParker.Features.Admin;

public sealed record AdminOverview(
    long RefundsPending, long ClaimsPending, long OverdueSessions, long OpenIncidents, long NoShowsToday,
    long ReservationsToday, long SessionsToday, long RevenueTodayCents, LotCard[] Lots);

public sealed record AdminLotDetail(LotCard Lot, AdminLotSpot[] Spots, BookingView[] Upcoming);

public sealed record AdminLotSpot(Guid Id, string Label, int Level, DateTime? LastCheckpointAt, string? CheckpointNote, string Type, string State);

public sealed record AdminBookingRow(BookingView View, string? UserName, string? UserEmail);

public sealed record AdminBookingsPage(AdminBookingRow[] Data, int Page, bool HasMore);

public sealed record AdminBookingDetail(BookingView View, AdminUserRef? User);

public sealed record AdminUserRef(Guid Id, string Name, string Email);

public sealed record AdminSessionRow(Guid Id, string PlateNo, DateTime EnteredAtUtc, DateTime? ExitedAtUtc, string? CancelReason, string Status, string? LotName, string? SpotLabel);

public sealed record AdminSessionsPage(AdminSessionRow[] Open, int OpenPage, bool OpenHasMore, long OpenCount, AdminSessionRow[] Exited, int ExitedPage, bool ExitedHasMore, long ExitedCount);

public sealed record AdminRefundRow(
    Guid Id, Guid ReservationId, Guid PaymentId, string Rule, long AmountCents, string Reason, DateTime CreatedAt,
    string? DecisionNote, Guid? ApprovedByUserId, DateTime? PaidAt, string Status,
    string? LotName, string? SpotLabel, string? RequestedByName, long MaxCents);

public sealed record AdminRefundsPage(AdminRefundRow[] Pending, int PendingPage, bool PendingHasMore, AdminRefundRow[] Decided, int DecidedPage, bool DecidedHasMore);

public sealed record AdminUserRow(Guid Id, string Name, string Email, DateTime? EmailVerifiedAt, DateTime CreatedAt, string Role);

public sealed record AdminUsersPage(AdminUserRow[] Data, int Page, bool HasMore);

public sealed record AdminUserDetail(AdminUserRow User, BookingView[] Bookings);
