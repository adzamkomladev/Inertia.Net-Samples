using Inertia.Net;
using CarParker.Data;
using CarParker.Infrastructure;
using CarParker.Features.Bookings;
using CarParker.Features.Lots;

namespace CarParker.Features.Guard;

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
        var page = await GuardService.ConsoleAsync(db, lot, ct);
        if (page is null)
            return Render("guard/Console", new InertiaProps
            {
                ["lots"] = Array.Empty<LotOption>(),
                ["lotId"] = (Guid?)null,
                ["spots"] = Array.Empty<SpotView>(),
                ["queue"] = Array.Empty<QueueRow>(),
                ["openSessions"] = Array.Empty<SessionRow>(),
            });

        return Render("guard/Console", new InertiaProps
        {
            ["lots"] = page.Lots,
            ["lotId"] = page.LotId,
            ["spots"] = page.Spots,
            ["queue"] = page.Queue,
            ["openSessions"] = page.OpenSessions,
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
        var page = await GuardService.BoardAsync(db, ct);
        return Render("guard/Console", new InertiaProps
        {
            ["boardOnly"] = true,
            ["lots"] = page.Lots,
            ["openSessions"] = page.OpenSessions,
            ["exitedSessions"] = page.ExitedSessions,
        });
    }

    static async Task<IResult> IncidentsAsync(Guid? lot, string? status, AppDb db, CancellationToken ct)
    {
        var page = await GuardService.IncidentsPageAsync(db, lot, status, ct);
        return Render("guard/Incidents", new InertiaProps
        {
            ["lots"] = page.Lots,
            ["filterLot"] = lot,
            ["filterStatus"] = status,
            ["incidents"] = page.Incidents.Select(i => new
            {
                i.Id, i.LotId, i.Kind, i.PlateNo, i.Description, i.Note,
                status = i.Status,
                i.CreatedAt, i.ResolvedAt,
                lotName = i.LotName,
                reporterName = i.ReporterName,
            }),
            ["kinds"] = GuardService.IncidentKinds,
        });
    }

    static async Task<IResult> CreateIncidentAsync(IncidentInput input, HttpContext ctx, AppDb db, RealtimeEvents realtime, CancellationToken ct) =>
        Respond(ctx, await GuardService.CreateIncidentAsync(input, ctx.User.UserId(), db, realtime, ct));

    static async Task<IResult> ResolveIncidentAsync(Guid id, string? note, HttpContext ctx, AppDb db, RealtimeEvents realtime, CancellationToken ct) =>
        Respond(ctx, await GuardService.ResolveIncidentAsync(id, note, ctx.User.UserId(), db, realtime, ct));
}
