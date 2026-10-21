using System.Security.Claims;
using Microsoft.AspNetCore.DataProtection;
using SvelteMongoBlog.Data;
using SvelteMongoBlog.Infrastructure;
using static SvelteMongoBlog.Features.AuthHelpers;

namespace SvelteMongoBlog.Features;

public static class Auth
{
    public static void MapAuth(this IEndpointRouteBuilder app)
    {
        app.MapGet(
            "/login",
            (HttpContext ctx, string? returnUrl) =>
                ctx.User.Identity?.IsAuthenticated == true
                    ? Results.Redirect(DefaultRedirect(ctx))
                    : Render(
                        "Auth/Login",
                        new InertiaProps { ["returnUrl"] = SafeReturnUrl(returnUrl) }
                    )
        );
        app.MapPost("/login", LoginAsync)
            .WithInertiaValidation()
            .RequireRateLimiting("auth");
        app.MapGet(
            "/register",
            (HttpContext ctx) =>
                ctx.User.Identity?.IsAuthenticated == true
                    ? Results.Redirect(DefaultRedirect(ctx))
                    : Render("Auth/Register")
        );
        app.MapPost("/register", RegisterAsync)
            .WithInertiaPrecognition()
            .RequireRateLimiting("auth");
        app.MapPost("/logout", (Delegate)LogoutAsync);
        app.MapGet(
                "/email/verify",
                (HttpContext ctx) =>
                    ctx.User.HasClaim(VerifiedClaim, "true")
                        ? Results.Redirect(DefaultRedirect(ctx))
                        : Render("Auth/VerifyEmail")
            )
            .RequireAuthorization();
        app.MapPost("/email/verification-notification", SendVerificationAsync)
            .RequireAuthorization()
            .RequireRateLimiting("auth");
        // Works signed in or not (link may be opened in another browser). ValidatePrincipal refreshes the claim.
        app.MapGet("/email/verify/confirm", ConfirmEmailAsync);
    }

    static async Task<IResult> LoginAsync(LoginInput input, HttpContext ctx, AppDb db, CancellationToken ct) =>
        Respond(ctx, await AuthService.LoginAsync(input, ctx, db, ct));

    static async Task<IResult> RegisterAsync(RegisterInput input, HttpContext ctx, AppDb db, JobQueue jobs,
        IDataProtectionProvider dp, IConfiguration config, CancellationToken ct) =>
        Respond(ctx, await AuthService.RegisterAsync(input, ctx, db, jobs, dp, config, ct));

    static async Task<IResult> LogoutAsync(HttpContext ctx)
    {
        await AuthService.LogoutAsync(ctx);
        return Results.Redirect("/login");
    }

    static async Task<IResult> SendVerificationAsync(HttpContext ctx, AppDb db, JobQueue jobs,
        IDataProtectionProvider dp, IConfiguration config, CancellationToken ct) =>
        Respond(ctx, await AuthService.SendVerificationAsync(ctx, db, jobs, dp, config, ct));

    static async Task<IResult> ConfirmEmailAsync(string? token, HttpContext ctx, AppDb db,
        IDataProtectionProvider dp, CancellationToken ct) =>
        Respond(ctx, await AuthService.ConfirmEmailAsync(token, ctx, db, dp, ct));

    /// <summary>Maps a service outcome onto the Inertia contract: back+errors, flash-then-redirect, or back+flash.</summary>
    static IResult Respond(HttpContext ctx, Outcome outcome)
    {
        if (outcome.Errors is { } errors)
            return Back().WithErrors(errors);
        if (outcome.Redirect is { } url)
        {
            if (outcome.Flash is { } flash) ctx.Inertia().Flash("toast", flash);
            return Results.Redirect(url);
        }
        return outcome.Flash is { } toast ? Back().WithFlash("toast", toast) : Back();
    }

    static string DefaultRedirect(HttpContext ctx) =>
        string.Equals(ctx.User.FindFirstValue(RoleClaim), "guard", StringComparison.OrdinalIgnoreCase)
            ? "/guard"
            : string.Equals(ctx.User.FindFirstValue(RoleClaim), "admin", StringComparison.OrdinalIgnoreCase)
                ? "/admin"
                : "/lots";
}
