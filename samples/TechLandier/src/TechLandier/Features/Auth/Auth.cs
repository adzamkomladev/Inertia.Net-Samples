using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using TechLandier.Data;
using TechLandier.Infrastructure;
using static TechLandier.Features.AuthHelpers;

namespace TechLandier.Features;


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

    internal static async Task<IResult> LoginSubmitAsync(LoginInput input, HttpContext ctx, AppDb db, CancellationToken ct)
    {
        var result = await AuthService.LoginAsync(input, db, ct);
        if (result.User is null)
            return Finish(ctx, result.Result);
        await AuthHttp.SignInAsync(
            ctx,
            result.User,
            await AuthService.AvatarKeyAsync(db, result.User, ct),
            input.Remember
        );
        return Finish(ctx, result.Result);
    }

    internal static IResult RegisterAsync(HttpContext ctx) =>
        ctx.User.Identity?.IsAuthenticated == true
            ? Results.Redirect("/products")
            : Render("Auth/Register");

    internal static async Task<IResult> RegisterSubmitAsync(RegisterInput input, HttpContext ctx, AppDb db, JobQueue jobs, IDataProtectionProvider dp, IConfiguration config, CancellationToken ct)
    {
        var baseUrl =
            config.AppBaseUrl()
            ?? $"{ctx.Request.Scheme}://{ctx.Request.Host}{ctx.Request.PathBase}";
        var result = await AuthService.RegisterAsync(input, baseUrl, db, jobs, dp, ct);
        if (result.User is null)
            return Finish(ctx, result.Result);
        await AuthHttp.SignInAsync(ctx, result.User, null, persistent: false);
        return Finish(ctx, result.Result);
    }

    internal static async Task<IResult> LogoutAsync(HttpContext ctx)
    {
        await ctx.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return Results.Redirect("/login");
    }

    internal static IResult VerifyEmailAsync(HttpContext ctx) =>
        ctx.User.HasClaim(VerifiedClaim, "true")
            ? Results.Redirect("/products")
            : Render("Auth/VerifyEmail");

    internal static async Task<IResult> SendVerificationAsync(HttpContext ctx, AppDb db, JobQueue jobs, IDataProtectionProvider dp, IConfiguration config, CancellationToken ct)
    {
        var baseUrl =
            config.AppBaseUrl()
            ?? $"{ctx.Request.Scheme}://{ctx.Request.Host}{ctx.Request.PathBase}";
        return Finish(
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

    internal static async Task<IResult> ConfirmEmailAsync(string? token, HttpContext ctx, AppDb db, IDataProtectionProvider dp, CancellationToken ct) =>
        Finish(
            ctx,
            await AuthService.ConfirmEmailAsync(
                token,
                ctx.User.Identity?.IsAuthenticated == true,
                db,
                dp,
                ct
            )
        );
}
