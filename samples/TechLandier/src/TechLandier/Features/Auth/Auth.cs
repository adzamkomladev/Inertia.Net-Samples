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
                : Render("Auth/Login", new InertiaProps { ["returnUrl"] = AuthUtils.SafeReturnUrl(returnUrl) })).WithName("login");
        app.MapPost("/login", Login).WithInertiaValidation().RequireRateLimiting("auth").WithName("login.store");

        app.MapGet("/register", IResult (ClaimsPrincipal user) =>
            user.Identity?.IsAuthenticated == true ? Results.Redirect("/products") : Render("Auth/Register")).WithName("register");
        app.MapPost("/register", Register).WithInertiaPrecognition().RequireRateLimiting("register").WithName("register.store");

        app.MapPost("/logout", async (HttpContext ctx) =>
        {
            await ctx.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            // The cookie handler does not clear the request principal either: the inline login render and its
            // antiforgery token must see the signed-out request. Rotates the history encryption key, so Back on a
            // shared device cannot show the previous user's pages.
            ctx.User = new ClaimsPrincipal(new ClaimsIdentity(CookieAuthenticationDefaults.AuthenticationScheme));
            ctx.Inertia().ClearHistory();
            // An Inertia visit renders the login page inline; the auth prop already sees the signed-out request.
            if (ctx.InertiaRequest().IsInertia)
            {
                PageUrl.Next(ctx, "/login");
                PageUrl.RefreshXsrf(ctx); // the token was minted for the signed-in user
                return Render("Auth/Login", new InertiaProps { ["returnUrl"] = null });
            }
            return Results.Redirect("/login");
        }).WithName("logout");

        app.MapGet("/email/verify", IResult (ClaimsPrincipal user) =>
            user.HasClaim(AuthMappers.VerifiedClaim, "true") ? Results.Redirect("/products") : Render("Auth/VerifyEmail")).RequireAuthorization().WithName("email.verify");
        app.MapPost("/email/verification-notification", async (ClaimsPrincipal user, AuthService auth, CancellationToken ct) =>
                await auth.SendVerificationAsync(user.UserId(), ct))
            .RequireAuthorization()
            .RequireRateLimiting("auth")
            .WithName("email.verification-notification");
        // Works signed in or not: the link may be opened in another browser.
        app.MapGet("/email/verify/confirm", async (string? token, ClaimsPrincipal user, AuthService auth, CancellationToken ct) =>
            await auth.ConfirmEmailAsync(token, user.Identity?.IsAuthenticated == true, ct)).WithName("email.verify.confirm");
    }

    // Returns SaveResult for the FeatureResponses filter to map, or an IResult when the POST renders the next page inline.
    static async Task<object?> Login(LoginInput input, HttpContext ctx, AuthService auth, StoreService store, CancellationToken ct)
    {
        var login = await auth.LoginAsync(input, ct);
        if (login.User is not null) await AuthHttp.SignInAsync(ctx, login.User, login.AvatarKey, input.Remember);
        // Playbook: the customer login lands on the products page — an Inertia visit renders it inline instead of
        // redirecting and visiting it again. The auth prop reads the principal the sign-in just put on the request.
        // An admin home, a returnUrl target and plain form posts keep today's redirect (the filter maps SaveResult).
        if (login.Result is { Errors: null, RedirectUrl: "/products" } && ctx.InertiaRequest().IsInertia)
        {
            PageUrl.Next(ctx, "/products");
            // The browser holds the anonymous antiforgery token; the follow-up page load that used to refresh it is
            // gone, so the inline render re-issues the cookie for the signed-in identity.
            PageUrl.RefreshXsrf(ctx);
            return await Store.RenderProductsAsync(new ProductQuery(null, null, null, null, null, "new"), new PageRequest(), store, ct);
        }
        return login.Result;
    }

    // Returns SaveResult for the FeatureResponses filter to map, or an IResult when the POST renders the next page inline.
    static async Task<object?> Register(RegisterInput input, HttpContext ctx, AuthService auth, CancellationToken ct)
    {
        var registration = await auth.RegisterAsync(input, ct);
        if (registration.User is not null) await AuthHttp.SignInAsync(ctx, registration.User, null, persistent: false);
        // Playbook: an Inertia visit renders the verify page inline — the new session is already on the request and
        // the page needs no queries. A plain form post keeps the redirect (the filter maps SaveResult).
        if (registration.Result is { Errors: null, RedirectUrl: "/email/verify" } && ctx.InertiaRequest().IsInertia)
        {
            PageUrl.Next(ctx, "/email/verify");
            PageUrl.RefreshXsrf(ctx); // the token was minted for the anonymous visitor
            return Render("Auth/VerifyEmail");
        }
        return registration.Result;
    }
}
