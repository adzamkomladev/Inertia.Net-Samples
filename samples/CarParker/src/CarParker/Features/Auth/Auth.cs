using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using CarParker.Features.Lots;
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
                : Render("Auth/Login", new InertiaProps { ["returnUrl"] = SafeReturnUrl(returnUrl) })).WithName("login");
        app.MapPost("/login", LoginAsync).WithInertiaValidation().RequireRateLimiting("auth").WithName("login.store");
        app.MapGet("/register", (ClaimsPrincipal user) =>
            user.Identity?.IsAuthenticated == true ? Results.Redirect(DefaultRedirect(user)) : Render("Auth/Register")).WithName("register");
        app.MapPost("/register", RegisterAsync).WithInertiaPrecognition().RequireRateLimiting("register").WithName("register.store");
        app.MapPost("/logout", async (HttpContext ctx) =>
        {
            await ctx.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            // The cookie handler does not clear the request principal either: the inline login render and its
            // antiforgery token must see the signed-out request.
            ctx.User = new ClaimsPrincipal(new ClaimsIdentity(CookieAuthenticationDefaults.AuthenticationScheme));
            // An Inertia visit renders the login page inline; the auth prop already sees the signed-out request.
            if (ctx.InertiaRequest().IsInertia)
            {
                PageUrl.Next(ctx, "/login");
                PageUrl.RefreshXsrf(ctx);
                return Render("Auth/Login", new InertiaProps { ["returnUrl"] = null });
            }
            return Results.Redirect("/login");
        }).RequireRateLimiting("writes").WithName("logout");
        app.MapGet("/email/verify", (ClaimsPrincipal user) =>
                user.HasClaim(VerifiedClaim, "true") ? Results.Redirect(DefaultRedirect(user)) : Render("Auth/VerifyEmail"))
            .RequireAuthorization()
            .WithName("email.verify");
        app.MapPost("/email/verification-notification", async (AuthService svc, ClaimsPrincipal user, HttpContext ctx, CancellationToken ct) =>
                (await svc.SendVerificationAsync(user.UserId(), ct)).ToResult(ctx))
            .RequireAuthorization()
            .RequireRateLimiting("verification")
            .WithName("email.verification-notification");
        // Works signed in or not: the link may be opened in another browser. ValidatePrincipal refreshes the claim.
        app.MapGet("/email/verify/confirm", async (string? token, AuthService svc, ClaimsPrincipal user, HttpContext ctx, CancellationToken ct) =>
            (await svc.ConfirmEmailAsync(token, user.Identity?.IsAuthenticated == true, ct)).ToResult(ctx)).WithName("email.verify.confirm");

        // Password reset: ask for a link, then set a new password from it. Both work signed out; a signed-in visitor is sent home.
        app.MapGet("/forgot-password", (ClaimsPrincipal user) =>
            user.Identity?.IsAuthenticated == true ? Results.Redirect(DefaultRedirect(user)) : Render("Auth/ForgotPassword")).WithName("password.request");
        app.MapPost("/forgot-password", async (ForgotPasswordInput input, AuthService svc, HttpContext ctx, CancellationToken ct) =>
                (await svc.RequestPasswordResetAsync(input, ct)).ToResult(ctx))
            .WithInertiaValidation().RequireRateLimiting("password-reset").WithName("password.email");
        app.MapGet("/reset-password", async (string? token, AuthService svc, HttpContext ctx, CancellationToken ct) =>
            await svc.UserForResetTokenAsync(token, ct) is { } account
                ? Render("Auth/ResetPassword", new InertiaProps { ["token"] = token, ["email"] = account.Email, ["minLength"] = AuthService.MinPasswordLength(account) })
                : Outcome.FlashThenRedirect(new Toast("error", "That reset link has expired or was already used. Ask for a new one."), "/forgot-password").ToResult(ctx))
            .WithName("password.reset");
        app.MapPost("/reset-password", async (ResetPasswordInput input, AuthService svc, HttpContext ctx, CancellationToken ct) =>
                (await svc.ResetPasswordAsync(input, ct)).ToResult(ctx))
            .WithInertiaValidation().RequireRateLimiting("auth").WithName("password.update");
    }

    static async Task<IResult> LoginAsync(LoginInput input, AuthService svc, LotsService lots, HttpContext ctx, CancellationToken ct)
    {
        var (account, guarded) = await svc.LoginAsync(input, ct);
        if (account is null)
            return Outcome.Fail("email", guarded ?? "These credentials do not match our records.").ToResult(ctx);
        var principal = Principal(account.User, account.AvatarKey);
        await AuthHttp.SignInAsync(ctx, principal, input.Remember);
        // A requested local returnUrl wins; otherwise each role lands on its own home (guards at the gate, admins on the console).
        var target = SafeReturnUrl(input.ReturnUrl) ?? DefaultRedirect(principal);
        // Playbook: the customer login lands on the lots page — an Inertia visit renders it inline instead of
        // redirecting and visiting it again. Guard and admin homes, returnUrl targets and plain form posts keep the redirect.
        if (target == "/lots" && ctx.InertiaRequest().IsInertia)
        {
            PageUrl.Next(ctx, "/lots");
            PageUrl.RefreshXsrf(ctx); // the token was minted for the anonymous visitor
            return await Lots.Lots.RenderLotsIndexAsync(lots, ct);
        }
        return Outcome.RedirectTo(target).ToResult(ctx);
    }

    static async Task<IResult> RegisterAsync(RegisterInput input, AuthService svc, HttpContext ctx, CancellationToken ct)
    {
        var result = await svc.RegisterAsync(input, ct);
        if (result.Account is { } account) await AuthHttp.SignInAsync(ctx, Principal(account.User, account.AvatarKey), persistent: false);
        // Playbook: an Inertia visit renders the verify page inline — the new session is already on the request and
        // the page needs no queries. A plain form post keeps the redirect (ToResult maps the Outcome).
        if (result.Account is not null && result.Outcome.Redirect == "/email/verify" && ctx.InertiaRequest().IsInertia)
        {
            PageUrl.Next(ctx, "/email/verify");
            PageUrl.RefreshXsrf(ctx);
            return Render("Auth/VerifyEmail");
        }
        return result.Outcome.ToResult(ctx);
    }

    static string? SafeReturnUrl(string? url) =>
        url is { Length: > 0 } && Microsoft.AspNetCore.Http.HttpResults.RedirectHttpResult.IsLocalUrl(url) ? url : null;

    static string DefaultRedirect(ClaimsPrincipal user) => user.FindFirstValue(RoleClaim)?.ToLowerInvariant() switch
    {
        "guard" => "/guard",
        "admin" => "/admin",
        _ => "/lots",
    };
}
