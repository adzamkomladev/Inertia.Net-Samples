using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using TechLandier.Infrastructure;

namespace TechLandier.Features;

public static class Auth
{
    public static void MapAuth(this IEndpointRouteBuilder app)
    {
        app.MapGet("/login", IResult (ClaimsPrincipal user, string? returnUrl) =>
            user.Identity?.IsAuthenticated == true
                ? Results.Redirect("/products")
                : Render("Auth/Login", new InertiaProps { ["returnUrl"] = AuthUtils.SafeReturnUrl(returnUrl) }));
        app.MapPost("/login", Login).WithInertiaValidation().RequireRateLimiting("auth");

        app.MapGet("/register", IResult (ClaimsPrincipal user) =>
            user.Identity?.IsAuthenticated == true ? Results.Redirect("/products") : Render("Auth/Register"));
        app.MapPost("/register", Register).WithInertiaPrecognition().RequireRateLimiting("auth");

        app.MapPost("/logout", async (HttpContext ctx) =>
        {
            await ctx.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return Results.Redirect("/login");
        });

        app.MapGet("/email/verify", IResult (ClaimsPrincipal user) =>
            user.HasClaim(AuthMappers.VerifiedClaim, "true") ? Results.Redirect("/products") : Render("Auth/VerifyEmail")).RequireAuthorization();
        app.MapPost("/email/verification-notification", (ClaimsPrincipal user, HttpContext ctx, IConfiguration config, AuthService auth, CancellationToken ct) =>
                auth.SendVerificationAsync(user.UserId(), BaseUrl(ctx, config), ct))
            .RequireAuthorization()
            .RequireRateLimiting("auth");
        // Works signed in or not: the link may be opened in another browser.
        app.MapGet("/email/verify/confirm", (string? token, ClaimsPrincipal user, AuthService auth, CancellationToken ct) =>
            auth.ConfirmEmailAsync(token, user.Identity?.IsAuthenticated == true, ct));
    }

    static string BaseUrl(HttpContext ctx, IConfiguration config) =>
        config.AppBaseUrl() ?? $"{ctx.Request.Scheme}://{ctx.Request.Host}{ctx.Request.PathBase}";

    static async Task<SaveResult> Login(LoginInput input, HttpContext ctx, AuthService auth, CancellationToken ct)
    {
        var login = await auth.LoginAsync(input, ct);
        if (login.User is not null) await AuthHttp.SignInAsync(ctx, login.User, login.AvatarKey, input.Remember);
        return login.Result;
    }

    static async Task<SaveResult> Register(RegisterInput input, HttpContext ctx, IConfiguration config, AuthService auth, CancellationToken ct)
    {
        var registration = await auth.RegisterAsync(input, BaseUrl(ctx, config), ct);
        if (registration.User is not null) await AuthHttp.SignInAsync(ctx, registration.User, null, persistent: false);
        return registration.Result;
    }
}
