using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using MongoDB.Driver;
using SvelteMongoReplicasBlog.Data;
using SvelteMongoReplicasBlog.Infrastructure;
using static SvelteMongoReplicasBlog.Features.AuthHelpers;

namespace SvelteMongoReplicasBlog.Features;

public static class Auth
{
    public static void MapAuth(this IEndpointRouteBuilder app)
    {
        app.MapGet(
            "/login",
            (HttpContext ctx, string? returnUrl) =>
                ctx.User.Identity?.IsAuthenticated == true
                    ? Results.Redirect("/posts")
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
                    ? Results.Redirect("/posts")
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
                        ? Results.Redirect("/posts")
                        : Render("Auth/VerifyEmail")
            )
            .RequireAuthorization();
        app.MapPost("/email/verification-notification", SendVerificationAsync)
            .RequireAuthorization()
            .RequireRateLimiting("auth");
        // Works signed in or not (link may be opened in another browser). ValidatePrincipal refreshes the claim.
        app.MapGet("/email/verify/confirm", ConfirmEmailAsync);
    }

    static async Task<IResult> LoginAsync(
        LoginInput input,
        string? returnUrl,
        HttpContext ctx,
        AppDb db,
        CancellationToken ct
    )
    {
        var user = await AuthService.LoginAsync(input, db, ct);
        if (user is null)
            return Outcome
                .Fail("email", "These credentials do not match our records.")
                .Respond(ctx);
        await AuthHttp.SignInAsync(
            ctx,
            user,
            await AuthService.AvatarKeyAsync(db, user, ct),
            input.Remember
        );
        return Outcome
            .RedirectTo(SafeReturnUrl(input.ReturnUrl) ?? "/posts")
            .Respond(ctx);
    }

    static async Task<IResult> RegisterAsync(
        RegisterInput input,
        HttpContext ctx,
        AppDb db,
        JobQueue jobs,
        IDataProtectionProvider dp,
        IConfiguration config,
        CancellationToken ct
    )
    {
        var baseUrl =
            config.AppBaseUrl()
            ?? $"{ctx.Request.Scheme}://{ctx.Request.Host}{ctx.Request.PathBase}";
        var result = await AuthService.RegisterAsync(input, baseUrl, db, jobs, dp, ct);
        if (result.User is null)
            return result.Outcome.Respond(ctx);
        await AuthHttp.SignInAsync(ctx, result.User, null, persistent: false);
        return result.Outcome.Respond(ctx);
    }

    static async Task<IResult> LogoutAsync(HttpContext ctx)
    {
        await ctx.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        ctx.Inertia().ClearHistory();
        return Results.Redirect("/login");
    }

    static async Task<IResult> SendVerificationAsync(
        HttpContext ctx,
        AppDb db,
        JobQueue jobs,
        IDataProtectionProvider dp,
        IConfiguration config,
        CancellationToken ct
    )
    {
        var baseUrl =
            config.AppBaseUrl()
            ?? $"{ctx.Request.Scheme}://{ctx.Request.Host}{ctx.Request.PathBase}";
        return (
            await AuthService.SendVerificationAsync(
                ctx.User.UserId(),
                baseUrl,
                db,
                jobs,
                dp,
                ct
            )
        ).Respond(ctx);
    }

    static async Task<IResult> ConfirmEmailAsync(
        string? token,
        HttpContext ctx,
        AppDb db,
        IDataProtectionProvider dp,
        CancellationToken ct
    ) =>
        (
            await AuthService.ConfirmEmailAsync(
                token,
                ctx.User.Identity?.IsAuthenticated == true,
                db,
                dp,
                ct
            )
        ).Respond(ctx);
}
