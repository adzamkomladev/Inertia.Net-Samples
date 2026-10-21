using Microsoft.AspNetCore.DataProtection;
using VuePgBlog.Data;
using VuePgBlog.Infrastructure;
using static VuePgBlog.Features.AuthHelpers;

namespace VuePgBlog.Features;


public static class Auth
{
    public static void MapAuth(this IEndpointRouteBuilder app)
    {
        app.MapGet("/login", LoginAsync);
        app.MapPost("/login", LoginSubmitAsync)
            .WithInertiaValidation()
            .RequireRateLimiting("auth");
        app.MapGet("/register", RegisterAsync);
        app.MapPost("/register", RegisterSubmitAsync)
            .WithInertiaPrecognition()
            .RequireRateLimiting("auth");
        app.MapPost("/logout", (Delegate)LogoutAsync);
        app.MapGet("/email/verify", VerifyEmailAsync)
            .RequireAuthorization();
        app.MapPost("/email/verification-notification", SendVerificationAsync)
            .RequireAuthorization()
            .RequireRateLimiting("auth");
        // Works signed in or not (link may be opened in another browser). ValidatePrincipal refreshes the claim.
        app.MapGet("/email/verify/confirm", ConfirmEmailAsync);
    }

    // Error outcomes replay Back().WithErrors; redirect outcomes write the toast first, then redirect.
    static IResult Finish(HttpContext ctx, SaveResult r)
    {
        if (r.RedirectUrl is { } url)
        {
            if (r.FlashType is { } redirectFlash) ctx.Inertia().Flash("toast", new Toast(redirectFlash, r.FlashMessage!));
            return Results.Redirect(url);
        }
        if (r.Errors is { } errors) return Back().WithErrors(errors);
        if (r.FlashType is { } type) return Back().WithFlash("toast", new Toast(type, r.FlashMessage!));
        return Back();
    }

    internal static IResult LoginAsync(HttpContext ctx, string? returnUrl) =>
        ctx.User.Identity?.IsAuthenticated == true
            ? Results.Redirect("/products")
            : Render("Auth/Login", new InertiaProps { ["returnUrl"] = SafeReturnUrl(returnUrl) });

    internal static async Task<IResult> LoginSubmitAsync(LoginInput input, HttpContext ctx, AppDb db, CancellationToken ct) =>
        Finish(ctx, await AuthService.LoginAsync(input, ctx, db, ct));

    internal static IResult RegisterAsync(HttpContext ctx) =>
        ctx.User.Identity?.IsAuthenticated == true
            ? Results.Redirect("/products")
            : Render("Auth/Register");

    internal static async Task<IResult> RegisterSubmitAsync(RegisterInput input, HttpContext ctx, AppDb db, JobQueue jobs, IDataProtectionProvider dp, IConfiguration config, CancellationToken ct) =>
        Finish(ctx, await AuthService.RegisterAsync(input, ctx, db, jobs, dp, config, ct));

    internal static Task<IResult> LogoutAsync(HttpContext ctx) => AuthService.LogoutAsync(ctx);

    internal static IResult VerifyEmailAsync(HttpContext ctx) =>
        ctx.User.HasClaim(VerifiedClaim, "true")
            ? Results.Redirect("/products")
            : Render("Auth/VerifyEmail");

    internal static async Task<IResult> SendVerificationAsync(HttpContext ctx, AppDb db, JobQueue jobs, IDataProtectionProvider dp, IConfiguration config, CancellationToken ct) =>
        Finish(ctx, await AuthService.SendVerificationAsync(ctx, db, jobs, dp, config, ct));

    internal static async Task<IResult> ConfirmEmailAsync(string? token, HttpContext ctx, AppDb db, IDataProtectionProvider dp, CancellationToken ct) =>
        Finish(ctx, await AuthService.ConfirmEmailAsync(token, ctx, db, dp, ct));
}
