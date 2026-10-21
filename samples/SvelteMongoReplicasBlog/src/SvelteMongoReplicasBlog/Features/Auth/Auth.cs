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
    ) => (await AuthService.LoginAsync(input, ctx, db, ct)).Respond(ctx);

    static async Task<IResult> RegisterAsync(
        RegisterInput input,
        HttpContext ctx,
        AppDb db,
        JobQueue jobs,
        IDataProtectionProvider dp,
        IConfiguration config,
        CancellationToken ct
    ) => (await AuthService.RegisterAsync(input, ctx, db, jobs, dp, config, ct)).Respond(ctx);

    static async Task<IResult> LogoutAsync(HttpContext ctx)
    {
        await AuthService.LogoutAsync(ctx);
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
    ) => (await AuthService.SendVerificationAsync(ctx, db, jobs, dp, config, ct)).Respond(ctx);

    static async Task<IResult> ConfirmEmailAsync(
        string? token,
        HttpContext ctx,
        AppDb db,
        IDataProtectionProvider dp,
        CancellationToken ct
    ) => (await AuthService.ConfirmEmailAsync(token, ctx, db, dp, ct)).Respond(ctx);
}
