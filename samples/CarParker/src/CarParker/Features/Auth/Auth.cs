using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using CarParker.Infrastructure;
using static CarParker.Features.AuthHelpers;

namespace CarParker.Features;

public static class Auth
{
    public static void MapAuth(this IEndpointRouteBuilder app)
    {
        app.MapGet("/login", (ClaimsPrincipal user, string? returnUrl) =>
            user.Identity?.IsAuthenticated == true
                ? Results.Redirect(DefaultRedirect(user))
                : Render("Auth/Login", new InertiaProps { ["returnUrl"] = SafeReturnUrl(returnUrl) }));
        app.MapPost("/login", LoginAsync).WithInertiaValidation().RequireRateLimiting("auth");
        app.MapGet("/register", (ClaimsPrincipal user) =>
            user.Identity?.IsAuthenticated == true ? Results.Redirect(DefaultRedirect(user)) : Render("Auth/Register"));
        app.MapPost("/register", RegisterAsync).WithInertiaPrecognition().RequireRateLimiting("register");
        app.MapPost("/logout", async (HttpContext ctx) =>
        {
            await ctx.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return Results.Redirect("/login");
        });
        app.MapGet("/email/verify", (ClaimsPrincipal user) =>
                user.HasClaim(VerifiedClaim, "true") ? Results.Redirect(DefaultRedirect(user)) : Render("Auth/VerifyEmail"))
            .RequireAuthorization();
        app.MapPost("/email/verification-notification", async (AuthService svc, ClaimsPrincipal user, HttpContext ctx, IConfiguration config, CancellationToken ct) =>
                (await svc.SendVerificationAsync(user.UserId(), BaseUrl(ctx, config), ct)).ToResult(ctx))
            .RequireAuthorization()
            .RequireRateLimiting("auth");
        // Works signed in or not: the link may be opened in another browser. ValidatePrincipal refreshes the claim.
        app.MapGet("/email/verify/confirm", async (string? token, AuthService svc, ClaimsPrincipal user, HttpContext ctx, CancellationToken ct) =>
            (await svc.ConfirmEmailAsync(token, user.Identity?.IsAuthenticated == true, ct)).ToResult(ctx));
    }

    static async Task<IResult> LoginAsync(LoginInput input, AuthService svc, HttpContext ctx, CancellationToken ct)
    {
        var account = await svc.LoginAsync(input, ct);
        if (account is null) return Outcome.Fail("email", "These credentials do not match our records.").ToResult(ctx);
        await AuthHttp.SignInAsync(ctx, Principal(account.User, account.AvatarKey), input.Remember);
        return Outcome.RedirectTo(SafeReturnUrl(input.ReturnUrl) ?? "/lots").ToResult(ctx);
    }

    static async Task<IResult> RegisterAsync(RegisterInput input, AuthService svc, HttpContext ctx, IConfiguration config, CancellationToken ct)
    {
        var result = await svc.RegisterAsync(input, BaseUrl(ctx, config), ct);
        if (result.Account is { } account) await AuthHttp.SignInAsync(ctx, Principal(account.User, account.AvatarKey), persistent: false);
        return result.Outcome.ToResult(ctx);
    }

    static string BaseUrl(HttpContext ctx, IConfiguration config) =>
        config.AppBaseUrl() ?? $"{ctx.Request.Scheme}://{ctx.Request.Host}{ctx.Request.PathBase}";

    static string? SafeReturnUrl(string? url) =>
        url is { Length: > 0 } && Microsoft.AspNetCore.Http.HttpResults.RedirectHttpResult.IsLocalUrl(url) ? url : null;

    static string DefaultRedirect(ClaimsPrincipal user) => user.FindFirstValue(RoleClaim)?.ToLowerInvariant() switch
    {
        "guard" => "/guard",
        "admin" => "/admin",
        _ => "/lots",
    };
}
