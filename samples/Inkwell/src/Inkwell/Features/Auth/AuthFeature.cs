using System.Security.Claims;
using Inkwell.Data;
using Inkwell.Features.Posts;
using Inkwell.Infrastructure;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace Inkwell.Features.Auth;

public static class AuthFeature
{
    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/login", (ClaimsPrincipal user) => user.Identity?.IsAuthenticated == true ? Results.Redirect("/posts") : Render("Auth/Login")).WithName("login");
        app.MapPost("/login", async ([FromBody] LoginInput input, string? returnUrl, AuthService auth, PostsService posts, HttpContext ctx, CancellationToken ct) =>
        {
            var outcome = await auth.LoginAsync(input, returnUrl);
            // The common login lands on the feed: an Inertia visit renders it inline instead of redirecting and
            // visiting it again (playbook: return the next page from the POST). The auth prop reads the principal the
            // sign-in just put on the request, so the page is already signed in. A plain form post keeps the redirect,
            // and a returnUrl points at a different page, so both keep today's response.
            if (outcome is { Errors: null, Redirect: "/posts" } && ctx.InertiaRequest().IsInertia)
            {
                PageUrl.Next(ctx, "/posts");
                // The browser holds the anonymous antiforgery token; the follow-up page load that used to refresh it
                // is gone, so the inline render re-issues the cookie for the signed-in identity.
                PageUrl.RefreshXsrf(ctx);
                return await PostsFeature.RenderFeedAsync(posts, ctx.User, ct);
            }
            return outcome.Respond(ctx);
        })
        .WithInertiaValidation()
        .RequireRateLimiting(RateLimits.Auth)
        .WithName("login.store");
        app.MapGet("/register", (ClaimsPrincipal user) => user.Identity?.IsAuthenticated == true ? Results.Redirect("/posts") : Render("Auth/Register")).WithName("register");
        app.MapPost("/register", async ([FromBody] RegisterInput input, AuthService auth, HttpContext ctx) =>
        {
            var outcome = await auth.RegisterAsync(input);
            // An Inertia visit renders the verify page inline: the new session is already on the request and the page
            // needs no queries. A plain form post keeps the redirect.
            if (outcome is { Errors: null, Redirect: "/email/verify" } && ctx.InertiaRequest().IsInertia)
            {
                if (outcome.Flash is { } flash) ctx.Inertia().Flash(flash.Key, flash.Message);
                PageUrl.Next(ctx, "/email/verify");
                PageUrl.RefreshXsrf(ctx); // the token was minted for the anonymous visitor
                return Render("Auth/VerifyEmail", new VerifyEmailProps(input.Email, false));
            }
            return outcome.Respond(ctx);
        })
        .WithInertiaPrecognition()
        .RequireRateLimiting(RateLimits.Register)
        .WithName("register.store");
        app.MapPost("/logout", async (AuthService auth, HttpContext ctx) =>
        {
            await auth.LogoutAsync();
            ctx.Inertia().ClearHistory();
            // An Inertia visit renders the login page inline; the auth prop already sees the signed-out request.
            if (ctx.InertiaRequest().IsInertia)
            {
                PageUrl.Next(ctx, "/login");
                PageUrl.RefreshXsrf(ctx); // the token was minted for the signed-in user
                return Render("Auth/Login");
            }
            return Results.Redirect("/login");
        }).WithName("logout");
        app.MapGet("/email/verify", async (AuthService auth, UserManager<AppUser> users, ClaimsPrincipal principal) =>
        {
            var (redirect, email) = await auth.ShowVerificationAsync(await users.GetUserAsync(principal), principal.IsVerified());
            return redirect is not null ? Results.Redirect(redirect) : Render("Auth/VerifyEmail", new VerifyEmailProps(email!, false));
        }).RequireAuthorization().WithName("email.verify");
        app.MapGet("/email/verify/{userId:guid}/{token}", async (Guid userId, string token, AuthService auth, ClaimsPrincipal user, HttpContext ctx, CancellationToken ct) =>
            (await auth.ConfirmEmailAsync(userId, token, user.UserId(), ct)).Respond(ctx)).WithName("email.verify.confirm");
        app.MapPost("/email/verification-notification", async (AuthService auth, UserManager<AppUser> users, ClaimsPrincipal user, HttpContext ctx) =>
            (await auth.SendVerificationAsync(await users.GetUserAsync(user))).Respond(ctx))
            .RequireAuthorization()
            .RequireRateLimiting(RateLimits.Auth)
            .WithName("email.verification-notification");
        return app;
    }
}
