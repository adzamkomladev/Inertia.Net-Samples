using System.Security.Claims;
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
        app.MapPost("/login", AuthService.LoginAsync)
            .WithInertiaValidation()
            .RequireRateLimiting("auth");
        app.MapGet(
            "/register",
            (HttpContext ctx) =>
                ctx.User.Identity?.IsAuthenticated == true
                    ? Results.Redirect(DefaultRedirect(ctx))
                    : Render("Auth/Register")
        );
        app.MapPost("/register", AuthService.RegisterAsync)
            .WithInertiaPrecognition()
            .RequireRateLimiting("auth");
        app.MapPost("/logout", (Delegate)AuthService.LogoutAsync);
        app.MapGet(
                "/email/verify",
                (HttpContext ctx) =>
                    ctx.User.HasClaim(VerifiedClaim, "true")
                        ? Results.Redirect(DefaultRedirect(ctx))
                        : Render("Auth/VerifyEmail")
            )
            .RequireAuthorization();
        app.MapPost("/email/verification-notification", AuthService.SendVerificationAsync)
            .RequireAuthorization()
            .RequireRateLimiting("auth");
        // Works signed in or not (link may be opened in another browser). ValidatePrincipal refreshes the claim.
        app.MapGet("/email/verify/confirm", AuthService.ConfirmEmailAsync);
    }

    static string DefaultRedirect(HttpContext ctx) =>
        string.Equals(ctx.User.FindFirstValue(RoleClaim), "guard", StringComparison.OrdinalIgnoreCase)
            ? "/guard"
            : string.Equals(ctx.User.FindFirstValue(RoleClaim), "admin", StringComparison.OrdinalIgnoreCase)
                ? "/admin"
                : "/lots";
}
