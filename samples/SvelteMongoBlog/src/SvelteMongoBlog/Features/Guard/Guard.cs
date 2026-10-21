using Inertia.Net;
using MongoDB.Driver;
using SvelteMongoBlog.Data;
using SvelteMongoBlog.Infrastructure;
using SvelteMongoBlog.Features.Bookings;
using SvelteMongoBlog.Features.Lots;

namespace SvelteMongoBlog.Features.Guard;

/// <summary>The scanner console: lot picker + live grid + today's scheduled queue, actions, board, incidents.</summary>
public static class Guard
{
    public static void MapGuard(this IEndpointRouteBuilder app)
    {
        app.MapGet("/guard", ConsoleAsync).RequireAuthorization("guard");
        app.MapPost("/guard/check-in", CheckInAsync).RequireAuthorization("guard");
        app.MapPost("/guard/check-out", CheckOutAsync).RequireAuthorization("guard");
        app.MapPost("/guard/sessions/{sessionId:guid}/resolve", ResolveOverstayAsync).RequireAuthorization("guard");
        app.MapGet("/guard/board", BoardAsync).RequireAuthorization("guard");
        app.MapGet("/guard/incidents", IncidentsAsync).RequireAuthorization("guard");
        app.MapPost("/guard/incidents", CreateIncidentAsync).RequireAuthorization("guard");
        app.MapPost("/guard/incidents/{id:guid}/resolve", ResolveIncidentAsync).RequireAuthorization("guard");
    }

    static async Task<IResult> ConsoleAsync(Guid? lot, AppDb db, CancellationToken ct)
    {
        var lots = await LotsService.ActiveAsync(db, ct);
        var lotId = lot is { } pick && lots.Any(l => l.Id == pick) ? pick : lots.FirstOrDefault()?.Id;
        var chosenLot = lots.FirstOrDefault(l => l.Id == lotId) ?? lots.FirstOrDefault();
        if (chosenLot is null)
            return Render("guard/Console", new InertiaProps
            {
                ["lots"] = Array.Empty<LotOption>(),
                ["lotId"] = (Guid?)null,
                ["spots"] = Array.Empty<SpotView>(),
                ["queue"] = Array.Empty<QueueRow>(),
                ["openSessions"] = Array.Empty<SessionRow>(),
            });

        var today = DateTime.UtcNow.Date;
        var tomorrow = today.AddDays(1);
        var reservations = await db.Reservations.Find(r => r.LotId == chosenLot.Id && r.Status == ReservationStatus.Confirmed)
            .Project(r => new { r.Id, r.SpotId, r.PlateNo }).ToListAsync(ct);
        var reservationIds = reservations.Select(r => r.Id).ToArray();
        var reservationById = reservations.ToDictionary(r => r.Id);
        var spotLabels = await db.Spots.Find(s => s.LotId == chosenLot.Id)
            .Project(s => new { s.Id, s.Label }).ToListAsync(ct);
        var labelsById = spotLabels.ToDictionary(x => x.Id, x => x.Label);
        var occurrences = reservationIds.Length == 0
            ? []
            : await db.Occurrences.Find(o => reservationIds.Contains(o.ReservationId)
                    && (o.Status == OccurrenceStatus.Scheduled || o.Status == OccurrenceStatus.Active)
                    && o.StartAtUtc < tomorrow && o.EndAtUtc >= today)
                .SortBy(o => o.StartAtUtc).ToListAsync(ct);
        var openSessions = await db.ParkingSessions.Find(s => s.LotId == chosenLot.Id && s.Status == SessionStatus.InProgress)
            .SortByDescending(s => s.EnteredAtUtc).ToListAsync(ct);

        return Render("guard/Console", new InertiaProps
        {
            ["lots"] = LotsService.OptionsFor(lots),
            ["lotId"] = chosenLot.Id,
            ["spots"] = await LotsService.SpotsAsync(db, chosenLot.Id, ct),
            ["queue"] = occurrences.Select(o => new QueueRow(
                o.Id,
                o.ReservationId,
                labelsById.GetValueOrDefault(reservationById.GetValueOrDefault(o.ReservationId).SpotId),
                o.StartAtUtc,
                o.EndAtUtc,
                o.Status.ToString().ToLowerInvariant(),
                reservationById.GetValueOrDefault(o.ReservationId).PlateNo)).ToArray(),
            ["openSessions"] = openSessions.Select(s => new SessionRow(
                s.Id,
                labelsById.GetValueOrDefault(s.SpotId),
                s.PlateNo,
                s.EnteredAtUtc,
                s.CancelReason)).ToArray(),
            ["grace"] = new { checkIn = GuardService.GraceMinutes, overstay = GuardService.OverstayGraceMinutes, noShow = GuardService.NoShowMinutes },
        });
    }

    static async Task<IResult> CheckInAsync(CheckInOutInput input, HttpContext ctx, AppDb db, RealtimeEvents realtime, CancellationToken ct) =>
        Respond(ctx, await GuardService.CheckInAsync(input, ctx.User.UserId(), db, realtime, ct));

    static async Task<IResult> CheckOutAsync(CheckInOutInput input, HttpContext ctx, AppDb db, RealtimeEvents realtime, JobQueue jobs, CancellationToken ct) =>
        Respond(ctx, await GuardService.CheckOutAsync(input, ctx.User.UserId(), db, realtime, jobs, ct: ct));

    static async Task<IResult> ResolveOverstayAsync(Guid sessionId, HttpContext ctx, AppDb db, RealtimeEvents realtime,
        JobQueue jobs, string? note, CancellationToken ct) =>
        Respond(ctx, await GuardService.ResolveOverstayAsync(sessionId, note, ctx.User.UserId(), db, realtime, jobs, ct));

    /// <summary>Maps a service outcome onto the Inertia contract: 404 page, back+errors, flash-then-redirect, or back+flash.</summary>
    static IResult Respond(HttpContext ctx, Outcome outcome)
    {
        if (outcome.NotFound)
            return Render("Error", new InertiaProps { ["status"] = 404 }).WithStatusCode(404);
        if (outcome.Forbidden)
            return HttpExtensions.Forbidden();
        if (outcome.Errors is { } errors)
            return Back().WithErrors(errors);
        if (outcome.Redirect is { } url)
        {
            if (outcome.Flash is { } flash) ctx.Inertia().Flash("toast", flash);
            return Results.Redirect(url);
        }
        return outcome.Flash is { } toast ? Back().WithFlash("toast", toast) : Back();
    }

    /// <summary>GET /guard/board — compact sessions list for today (open first, exited after).</summary>
    static async Task<IResult> BoardAsync(AppDb db, CancellationToken ct)
    {
        var lots = await LotsService.ActiveAsync(db, ct);
        var lotIds = lots.Select(l => l.Id).ToArray();
        var open = await db.ParkingSessions.Find(s => lotIds.Contains(s.LotId) && s.Status == SessionStatus.InProgress)
            .SortBy(s => s.EnteredAtUtc).ToListAsync(ct);
        var today = DateTime.UtcNow.Date;
        var exited = await db.ParkingSessions.Find(s => lotIds.Contains(s.LotId) && s.Status == SessionStatus.Exited
                && s.EnteredAtUtc >= today).SortByDescending(s => s.EnteredAtUtc).Limit(50).ToListAsync(ct);
        var spotIds = open.Concat(exited).Select(s => s.SpotId).Distinct().ToArray();
        var labels = spotIds.Length == 0 ? [] : await db.Spots.Find(s => spotIds.Contains(s.Id))
            .Project(s => new { s.Id, s.Label }).ToListAsync(ct);
        var labelsById = labels.ToDictionary(x => x.Id, x => x.Label);
        return Render("guard/Console", new InertiaProps
        {
            ["boardOnly"] = true,
            ["lots"] = LotsService.OptionsFor(lots),
            ["openSessions"] = open.Select(s => new SessionRow(
                s.Id, labelsById.GetValueOrDefault(s.SpotId), s.PlateNo, s.EnteredAtUtc, s.CancelReason)).ToArray(),
            ["exitedSessions"] = exited.Select(s => new SessionRow(
                s.Id, labelsById.GetValueOrDefault(s.SpotId), s.PlateNo, s.EnteredAtUtc, s.CancelReason, s.ExitedAtUtc)).ToArray(),
        });
    }

    static async Task<IResult> IncidentsAsync(Guid? lot, string? status, AppDb db, CancellationToken ct)
    {
        var lots = await LotsService.ActiveAsync(db, ct);
        var incidents = await GuardService.IncidentsAsync(db, lot, status, ct);
        var lotNames = lots.ToDictionary(l => l.Id, l => l.Name);
        var reporterIds = incidents.Select(i => i.ReporterUserId).Distinct().ToArray();
        var reporters = reporterIds.Length == 0 ? [] : await db.Users.Find(u => reporterIds.Contains(u.Id))
            .Project(u => new { u.Id, u.Name }).ToListAsync(ct);
        var namesById = reporters.ToDictionary(x => x.Id, x => x.Name);
        return Render("guard/Incidents", new InertiaProps
        {
            ["lots"] = LotsService.OptionsFor(lots),
            ["filterLot"] = lot,
            ["filterStatus"] = status,
            ["incidents"] = incidents.Select(i => new
            {
                i.Id, i.LotId, i.Kind, i.PlateNo, i.Description, i.Note,
                status = i.Status.ToString().ToLowerInvariant(),
                i.CreatedAt, i.ResolvedAt,
                lotName = lotNames.GetValueOrDefault(i.LotId),
                reporterName = namesById.GetValueOrDefault(i.ReporterUserId),
            }),
            ["kinds"] = GuardService.IncidentKinds,
        });
    }

    static async Task<IResult> CreateIncidentAsync(IncidentInput input, HttpContext ctx, AppDb db, RealtimeEvents realtime, CancellationToken ct) =>
        Respond(ctx, await GuardService.CreateIncidentAsync(input, ctx.User.UserId(), db, realtime, ct));

    static async Task<IResult> ResolveIncidentAsync(Guid id, string? note, HttpContext ctx, AppDb db, RealtimeEvents realtime, CancellationToken ct) =>
        Respond(ctx, await GuardService.ResolveIncidentAsync(id, note, ctx.User.UserId(), db, realtime, ct));

    internal sealed record QueueRow(Guid Id, Guid ReservationId, string? SpotLabel, DateTime StartAtUtc, DateTime EndAtUtc, string Status, string? PlateNo);

    internal sealed record SessionRow(Guid Id, string? SpotLabel, string PlateNo, DateTime EnteredAtUtc, string? CancelReason, DateTime? ExitedAtUtc = null);
}
