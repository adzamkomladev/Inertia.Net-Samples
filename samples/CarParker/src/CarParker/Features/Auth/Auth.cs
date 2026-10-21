using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using CarParker.Data;
using CarParker.Infrastructure;
using static CarParker.Features.AuthHelpers;

namespace CarParker.Features;

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

    static async Task<IResult> LoginAsync(LoginInput input, HttpContext ctx, AppDb db, CancellationToken ct)
    {
        var user = await AuthService.LoginAsync(input, db, ct);
        if (user is null)
            return Respond(ctx, Outcome.Fail("email", "These credentials do not match our records."));
        await AuthHttp.SignInAsync(
            ctx,
            user,
            await AuthService.AvatarKeyAsync(db, user, ct),
            input.Remember
        );
        return Respond(ctx, Outcome.RedirectTo(SafeReturnUrl(input.ReturnUrl) ?? "/lots"));
    }

    static async Task<IResult> RegisterAsync(RegisterInput input, HttpContext ctx, AppDb db, JobQueue jobs,
        IDataProtectionProvider dp, IConfiguration config, CancellationToken ct)
    {
        var baseUrl =
            config.AppBaseUrl()
            ?? $"{ctx.Request.Scheme}://{ctx.Request.Host}{ctx.Request.PathBase}";
        var result = await AuthService.RegisterAsync(input, baseUrl, db, jobs, dp, ct);
        if (result.User is null)
            return Respond(ctx, result.Outcome);
        await AuthHttp.SignInAsync(ctx, result.User, null, persistent: false);
        return Respond(ctx, result.Outcome);
    }

    static async Task<IResult> LogoutAsync(HttpContext ctx)
    {
        await ctx.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return Results.Redirect("/login");
    }

    static async Task<IResult> SendVerificationAsync(HttpContext ctx, AppDb db, JobQueue jobs,
        IDataProtectionProvider dp, IConfiguration config, CancellationToken ct)
    {
        var baseUrl =
            config.AppBaseUrl()
            ?? $"{ctx.Request.Scheme}://{ctx.Request.Host}{ctx.Request.PathBase}";
        return Respond(
            ctx,
            await AuthService.SendVerificationAsync(
                ctx.User.UserId(),
                baseUrl,
                db,
                jobs,
                dp,
                ct
            )
        );
    }

    static async Task<IResult> ConfirmEmailAsync(string? token, HttpContext ctx, AppDb db,
        IDataProtectionProvider dp, CancellationToken ct) =>
        Respond(
            ctx,
            await AuthService.ConfirmEmailAsync(
                token,
                ctx.User.Identity?.IsAuthenticated == true,
                db,
                dp,
                ct
            )
        );

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
