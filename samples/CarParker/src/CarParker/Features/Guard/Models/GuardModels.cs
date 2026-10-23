using MongoDB.Driver;
using CarParker.Data;
using CarParker.Features.Alerts;
using CarParker.Features.Bookings;
using CarParker.Features.Lots;
using CarParker.Infrastructure;

namespace CarParker.Features.Guard;

public sealed record QueueRow(Guid Id, Guid ReservationId, string? SpotLabel, DateTime StartAtUtc, DateTime EndAtUtc, string Status, string? PlateNo);

public sealed record SessionRow(Guid Id, string? SpotLabel, string PlateNo, DateTime EnteredAtUtc, string? CancelReason, DateTime? ExitedAtUtc = null);

public sealed record IncidentRow(Guid Id, Guid LotId, string Kind, string? PlateNo, string Description, string? Note, string Status, DateTime CreatedAt, DateTime? ResolvedAt, string? LotName, string? ReporterName);

public sealed record ConsolePage(List<LotOption> Lots, Guid LotId, SpotView[] Spots, QueueRow[] Queue, SessionRow[] OpenSessions);

public sealed record BoardPage(List<LotOption> Lots, SessionRow[] OpenSessions, SessionRow[] ExitedSessions);

public sealed record IncidentsPage(List<LotOption> Lots, IncidentRow[] Incidents, int Page, bool HasMore);
