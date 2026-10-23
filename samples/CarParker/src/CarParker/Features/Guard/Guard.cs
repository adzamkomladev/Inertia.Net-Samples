using System.Security.Claims;
using CarParker.Features.Bookings;
using CarParker.Features.Lots;
using CarParker.Infrastructure;

namespace CarParker.Features.Guard;

/// <summary>The scanner console: lot picker, live grid, today's queue, check-in/out, board and incidents.</summary>
public static class Guard
{
    public static void MapGuard(this IEndpointRouteBuilder app)
    {
        var guard = app.MapGroup("/guard").RequireAuthorization("guard");
        guard.MapGet("/", ConsoleAsync);
        guard.MapPost("/check-in", async (CheckInOutInput input, GuardService svc, ClaimsPrincipal user, HttpContext ctx, CancellationToken ct) =>
            (await svc.CheckInAsync(input, user.UserId(), ct)).ToResult(ctx));
        guard.MapPost("/check-out", async (CheckInOutInput input, GuardService svc, ClaimsPrincipal user, HttpContext ctx, CancellationToken ct) =>
            (await svc.CheckOutAsync(input, user.UserId(), ct)).ToResult(ctx));
        guard.MapPost("/sessions/{sessionId:guid}/resolve", async (Guid sessionId, string? note, GuardService svc, ClaimsPrincipal user, HttpContext ctx, CancellationToken ct) =>
            (await svc.ResolveOverstayAsync(sessionId, note, user.UserId(), ct)).ToResult(ctx));
        guard.MapGet("/board", BoardAsync);
        guard.MapGet("/incidents", IncidentsAsync);
        guard.MapPost("/incidents", async (IncidentInput input, GuardService svc, ClaimsPrincipal user, HttpContext ctx, CancellationToken ct) =>
            (await svc.CreateIncidentAsync(input, user.UserId(), ct)).ToResult(ctx));
        guard.MapPost("/incidents/{id:guid}/resolve", async (Guid id, string? note, GuardService svc, HttpContext ctx, CancellationToken ct) =>
            (await svc.ResolveIncidentAsync(id, note, ct)).ToResult(ctx));
    }

    static async Task<IResult> ConsoleAsync(Guid? lot, GuardService svc, CancellationToken ct)
    {
        var page = await svc.ConsoleAsync(lot, ct);
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

    static async Task<IResult> BoardAsync(GuardService svc, CancellationToken ct)
    {
        var page = await svc.BoardAsync(ct);
        return Render("guard/Console", new InertiaProps
        {
            ["boardOnly"] = true,
            ["lots"] = page.Lots,
            ["openSessions"] = page.OpenSessions,
            ["exitedSessions"] = page.ExitedSessions,
        });
    }

    static async Task<IResult> IncidentsAsync(Guid? lot, string? status, string? q, int? page, GuardService svc, CancellationToken ct)
    {
        var result = await svc.IncidentsPageAsync(lot, status, q, page ?? 1, ct);
        return Render("guard/Incidents", new InertiaProps
        {
            ["lots"] = result.Lots,
            ["filterLot"] = lot,
            ["filterStatus"] = status,
            ["query"] = q ?? "",
            ["incidents"] = result.Incidents,
            ["page"] = result.Page,
            ["hasMore"] = result.HasMore,
            ["kinds"] = GuardService.IncidentKinds,
        });
    }
}
