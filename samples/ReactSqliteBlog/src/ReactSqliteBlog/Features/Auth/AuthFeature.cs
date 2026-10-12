using ReactSqliteBlog.Data;
using ReactSqliteBlog.Infrastructure;
using ReactSqliteBlog.Jobs;
using ReactSqliteBlog.Jobs.Handlers;
using Microsoft.AspNetCore.Identity;

namespace ReactSqliteBlog.Features.Auth;

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
        app.MapPost("/login", AuthService.LoginAsync)
            .WithInertiaValidation()
            .RequireRateLimiting(RateLimits.Auth);
        app.MapGet(
            "/register",
            (HttpContext ctx) =>
                ctx.User.Identity?.IsAuthenticated == true
                    ? Results.Redirect("/posts")
                    : Render("Auth/Register")
        );
        app.MapPost("/register", AuthService.RegisterAsync)
            .WithInertiaPrecognition()
            .RequireRateLimiting(RateLimits.Auth);
        app.MapPost("/logout", AuthService.LogoutAsync);
        app.MapGet("/email/verify", AuthService.ShowVerificationAsync).RequireAuthorization();
        app.MapGet("/email/verify/{userId:guid}/{token}", AuthService.ConfirmEmailAsync);
        app.MapPost("/email/verification-notification", AuthService.SendVerificationAsync)
            .RequireAuthorization()
            .RequireRateLimiting(RateLimits.Auth);
        return app;
    }
}
