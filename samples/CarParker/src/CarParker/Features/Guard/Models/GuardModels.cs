using MongoDB.Driver;
using CarParker.Data;
using CarParker.Features.Alerts;
using CarParker.Features.Bookings;
using CarParker.Features.Lots;
using CarParker.Infrastructure;

namespace CarParker.Features.Guard;

public sealed record QueueRow(Guid Id, Guid ReservationId, string? SpotLabel, DateTime StartAtUtc, DateTime EndAtUtc, string Status, string? PlateNo);

/// <summary>A parking session. WindowEndUtc (console rows) is the end of the booked window it belongs to, so overstay counts from it.</summary>
public sealed record SessionRow(Guid Id, string? SpotLabel, string PlateNo, DateTime EnteredAtUtc, string? CancelReason, DateTime? ExitedAtUtc = null, DateTime? WindowEndUtc = null);

public sealed record IncidentRow(Guid Id, Guid LotId, string Kind, string? PlateNo, string Description, string? Note, string Status, DateTime CreatedAt, DateTime? ResolvedAt, string? LotName, string? ReporterName, string LotTimeZoneId = "UTC");

/// <summary>The gate console. Times are UTC instants; LotTimeZoneId is the clock the guard reads them on.</summary>
public sealed record ConsolePage(List<LotOption> Lots, Guid LotId, SpotView[] Spots, QueueRow[] Queue, SessionRow[] OpenSessions, string LotTimeZoneId = "UTC");

public sealed record BoardPage(List<LotOption> Lots, SessionRow[] OpenSessions, SessionRow[] ExitedSessions);

public sealed record IncidentsPage(List<LotOption> Lots, IncidentRow[] Incidents, int Page, bool HasMore);
