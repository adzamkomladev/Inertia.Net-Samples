using System.Security.Claims;
using Inkwell.Data;
using Inkwell.Infrastructure;
using Inkwell.Jobs;
using Inkwell.Jobs.Handlers;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using static Inkwell.Features.Auth.AuthHelpers;

namespace Inkwell.Features.Auth;

public static class AuthFeature
{
    public static IServiceCollection AddAuthFeature(
        this IServiceCollection services,
        IConfiguration config
    )
    {
        // Registered after AddIdentityCore, so this one wins.
        services.AddScoped<IUserClaimsPrincipalFactory<AppUser>, AppClaimsPrincipalFactory>();
        services.Configure<EmailOptions>(config.GetSection("Email"));
        services.AddHttpClient<EmailSender>();
        services.AddJobHandler<SendVerificationEmail, SendVerificationEmailHandler>();
        services.AddJobHandler<CommentNotification, CommentNotificationHandler>();
        services.AddJobHandler<LikeNotification, LikeNotificationHandler>();
        services.AddJobHandler<WeeklyDigestEmail, WeeklyDigestEmailHandler>();
        return services;
    }

    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet(
            "/login",
            (HttpContext ctx) =>
                ctx.User.Identity?.IsAuthenticated == true
                    ? Results.Redirect("/posts")
                    : Render("Auth/Login")
        );
        app.MapPost("/login", LoginAsync)
            .WithInertiaValidation()
            .RequireRateLimiting(RateLimits.Auth);
        app.MapGet(
            "/register",
            (HttpContext ctx) =>
                ctx.User.Identity?.IsAuthenticated == true
                    ? Results.Redirect("/posts")
                    : Render("Auth/Register")
        );
        app.MapPost("/register", RegisterAsync)
            .WithInertiaPrecognition()
            .RequireRateLimiting(RateLimits.Auth);
        app.MapPost("/logout", LogoutAsync);
        app.MapGet("/email/verify", ShowVerificationAsync).RequireAuthorization();
        app.MapGet("/email/verify/{userId:guid}/{token}", ConfirmEmailAsync);
        app.MapPost("/email/verification-notification", SendVerificationAsync)
            .RequireAuthorization()
            .RequireRateLimiting(RateLimits.Auth);
        return app;
    }

    static async Task<IResult> LoginAsync(
        [FromBody] LoginInput input,
        string? returnUrl,
        HttpContext ctx,
        UserManager<AppUser> users,
        SignInManager<AppUser> signIn
    ) => (await AuthService.LoginAsync(input, returnUrl, users, signIn)).Respond(ctx);

    static async Task<IResult> RegisterAsync(
        [FromBody] RegisterInput input,
        HttpContext ctx,
        UserManager<AppUser> users,
        SignInManager<AppUser> signIn,
        AppDb db,
        JobQueue queue
    ) => (await AuthService.RegisterAsync(input, users, signIn, db, queue)).Respond(ctx);

    static async Task<IResult> LogoutAsync(HttpContext ctx, SignInManager<AppUser> signIn)
    {
        await AuthService.LogoutAsync(signIn);
        ctx.Inertia().ClearHistory();
        return Results.Redirect("/login");
    }

    static async Task<IResult> ShowVerificationAsync(
        HttpContext ctx,
        UserManager<AppUser> users,
        SignInManager<AppUser> signIn
    )
    {
        var user = await users.GetUserAsync(ctx.User);
        var view = await AuthService.ShowVerificationAsync(user, ctx.User.IsVerified(), signIn);
        return view.Redirect is { } url
            ? Results.Redirect(url)
            : Render("Auth/VerifyEmail", new VerifyEmailProps(view.Email!, false));
    }

    static async Task<IResult> ConfirmEmailAsync(
        Guid userId,
        string token,
        HttpContext ctx,
        UserManager<AppUser> users,
        SignInManager<AppUser> signIn
    ) =>
        (
            await AuthService.ConfirmEmailAsync(
                userId,
                token,
                ctx.User.UserId(),
                ctx.User.Identity?.IsAuthenticated == true,
                users,
                signIn
            )
        ).Respond(ctx);

    static async Task<IResult> SendVerificationAsync(
        HttpContext ctx,
        UserManager<AppUser> users,
        AppDb db,
        JobQueue queue
    ) => (await AuthService.SendVerificationAsync(await users.GetUserAsync(ctx.User), db, queue)).Respond(ctx);
}

/// <summary>Adds the display name and the <c>email_verified</c> claim used by the "verified" policy.</summary>
public sealed class AppClaimsPrincipalFactory(
    UserManager<AppUser> users,
    IOptions<IdentityOptions> options
) : UserClaimsPrincipalFactory<AppUser>(users, options)
{
    protected override async Task<ClaimsIdentity> GenerateClaimsAsync(AppUser user)
    {
        var id = await base.GenerateClaimsAsync(user);
        id.AddClaim(
            new Claim(AuthPolicies.EmailVerifiedClaim, user.EmailConfirmed ? "true" : "false")
        );
        id.AddClaim(new Claim("display_name", user.DisplayName));
        return id;
    }
}
