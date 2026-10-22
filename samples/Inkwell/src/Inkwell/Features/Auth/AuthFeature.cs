using System.Security.Claims;
using Inkwell.Data;
using Inkwell.Infrastructure;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace Inkwell.Features.Auth;

public static class AuthFeature
{
    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/login", (ClaimsPrincipal user) => user.Identity?.IsAuthenticated == true ? Results.Redirect("/posts") : Render("Auth/Login"));
        app.MapPost("/login", async ([FromBody] LoginInput input, string? returnUrl, AuthService auth, HttpContext ctx) =>
            (await auth.LoginAsync(input, returnUrl)).Respond(ctx))
            .WithInertiaValidation()
            .RequireRateLimiting(RateLimits.Auth);
        app.MapGet("/register", (ClaimsPrincipal user) => user.Identity?.IsAuthenticated == true ? Results.Redirect("/posts") : Render("Auth/Register"));
        app.MapPost("/register", async ([FromBody] RegisterInput input, AuthService auth, HttpContext ctx) =>
            (await auth.RegisterAsync(input)).Respond(ctx))
            .WithInertiaPrecognition()
            .RequireRateLimiting(RateLimits.Auth);
        app.MapPost("/logout", async (AuthService auth, HttpContext ctx) =>
        {
            await auth.LogoutAsync();
            ctx.Inertia().ClearHistory();
            return Results.Redirect("/login");
        });
        app.MapGet("/email/verify", async (AuthService auth, UserManager<AppUser> users, ClaimsPrincipal principal) =>
        {
            var (redirect, email) = await auth.ShowVerificationAsync(await users.GetUserAsync(principal), principal.IsVerified());
            return redirect is not null ? Results.Redirect(redirect) : Render("Auth/VerifyEmail", new VerifyEmailProps(email!, false));
        }).RequireAuthorization();
        app.MapGet("/email/verify/{userId:guid}/{token}", async (Guid userId, string token, AuthService auth, ClaimsPrincipal user, HttpContext ctx) =>
            (await auth.ConfirmEmailAsync(userId, token, user.UserId())).Respond(ctx));
        app.MapPost("/email/verification-notification", async (AuthService auth, UserManager<AppUser> users, ClaimsPrincipal user, HttpContext ctx) =>
            (await auth.SendVerificationAsync(await users.GetUserAsync(user))).Respond(ctx))
            .RequireAuthorization()
            .RequireRateLimiting(RateLimits.Auth);
        return app;
    }
}
