using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using VuePgReplicasAdvBlog.Data;
using VuePgReplicasAdvBlog.Features.Auth;
using VuePgReplicasAdvBlog.Infrastructure;
using VuePgReplicasAdvBlog.MainApp.Infrastructure;
using VuePgReplicasAdvBlog.MainApp.Requests;
using static VuePgReplicasAdvBlog.MainApp.Infrastructure.AuthHttp;

namespace VuePgReplicasAdvBlog.MainApp.Endpoints;

public static class AuthEndpoints
{
    public static void MapAuth(this IEndpointRouteBuilder app)
    {
        app.MapGet("/login", (HttpContext ctx, string? returnUrl) => ctx.User.Identity?.IsAuthenticated == true
            ? Results.Redirect("/posts") : Render("Auth/Login", new InertiaProps { ["returnUrl"] = SafeReturnUrl(returnUrl) }));
        app.MapGet("/register", (HttpContext ctx) => ctx.User.Identity?.IsAuthenticated == true
            ? Results.Redirect("/posts") : Render("Auth/Register"));
        app.MapPost("/login", LoginAsync).WithInertiaValidation().RequireRateLimiting("auth");
        app.MapPost("/register", RegisterAsync).WithInertiaPrecognition().RequireRateLimiting("auth");
        app.MapPost("/logout", (Delegate)LogoutAsync);
        app.MapGet("/email/verify", (HttpContext ctx) => ctx.User.HasClaim(AuthHelpers.VerifiedClaim, "true")
            ? Results.Redirect("/posts") : Render("Auth/VerifyEmail")).RequireAuthorization();
        app.MapPost("/email/verification-notification", SendVerificationAsync).RequireAuthorization().RequireRateLimiting("auth");
        app.MapGet("/email/verify/confirm", ConfirmEmailAsync);
    }

    static async Task<IResult> LoginAsync(LoginInput input, HttpContext ctx, AppDb db, CancellationToken ct)
    {
        var user = await AuthService.LoginAsync(input.Email!, input.Password!, db, ct);
        await SignInAsync(ctx, user, db, input.Remember, ct);
        return Results.Redirect(SafeReturnUrl(input.ReturnUrl) ?? "/posts");
    }

    static async Task<IResult> RegisterAsync(RegisterInput input, HttpContext ctx, AppDb db, JobQueue jobs, IDataProtectionProvider protection, IConfiguration config, CancellationToken ct)
    {
        var user = await AuthService.RegisterAsync(input.Name!, input.Email!, input.Password!, db, ct);
        await AuthService.SendVerificationAsync(user, VerificationUrl(ctx, config, protection, user), db, jobs, ct);
        await SignInAsync(ctx, user, db, false, ct);
        return Results.Redirect("/email/verify");
    }

    static async Task<IResult> LogoutAsync(HttpContext ctx)
    {
        await ctx.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return Results.Redirect("/login");
    }

    static async Task<IResult> SendVerificationAsync(HttpContext ctx, AppDb db, JobQueue jobs, IDataProtectionProvider protection, IConfiguration config, CancellationToken ct)
    {
        var user = await db.Users.FindAsync([ctx.User.UserId()], ct);
        if (user is null || user.EmailVerifiedAt is not null) return Results.Redirect("/posts");
        await AuthService.SendVerificationAsync(user, VerificationUrl(ctx, config, protection, user), db, jobs, ct);
        return Back().WithFlash("toast", new Toast("success", "A new verification link has been sent."));
    }

    static async Task<IResult> ConfirmEmailAsync(string? token, HttpContext ctx, AppDb db, IDataProtectionProvider protection, CancellationToken ct)
    {
        var result = await AuthService.ConfirmEmailAsync(token, db, protection, ct);
        var signedIn = ctx.User.Identity?.IsAuthenticated == true;
        var (type, message, path) = result switch
        {
            EmailConfirmation.InvalidToken => ("error", "That verification link is invalid or has expired.", signedIn ? "/email/verify" : "/login"),
            EmailConfirmation.UnknownRecipient => ("error", "That verification link is no longer valid.", "/login"),
            _ => ("success", "Email verified. Welcome aboard!", signedIn ? "/posts" : "/login")
        };
        ctx.Inertia().Flash("toast", new Toast(type, message));
        return Results.Redirect(path);
    }
}
