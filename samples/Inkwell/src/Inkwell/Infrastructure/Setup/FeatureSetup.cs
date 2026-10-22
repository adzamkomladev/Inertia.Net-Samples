using Inkwell.Cron;
using Inkwell.Data;
using Inkwell.Features.Analytics;
using Inkwell.Features.Auth;
using Inkwell.Features.Discovery;
using Inkwell.Features.Posts;
using Inkwell.Features.Settings;
using Inkwell.Features.Social;
using Inkwell.Features.Uploads;
using Inkwell.Jobs;
using Inkwell.Jobs.Handlers;
using Microsoft.AspNetCore.Identity;

namespace Inkwell.Infrastructure.Setup;

/// <summary>Services, background jobs, cron and storage.</summary>
public static class FeatureSetup
{
    public static WebApplicationBuilder AddBlogFeatures(this WebApplicationBuilder builder)
    {
        var services = builder.Services;
        var config = builder.Configuration;

        services.AddSignalR();
        services.AddSingleton<RealtimeEvents>();
        services.AddSingleton<RealtimeDelivery>();
        services.AddJobs();
        services.AddJobHandler<PostChanged, PostChangedHandler>();
        services.AddJobHandler<PostViewed, PostViewedHandler>();
        services.AddJobHandler<PostRemoved, PostRemovedHandler>();
        services.AddCron(config);
        services.AddStorage(config);

        // Registered after AddIdentityCore, so this factory wins.
        services.AddScoped<IUserClaimsPrincipalFactory<AppUser>, AppClaimsPrincipalFactory>();
        services.Configure<EmailOptions>(config.GetSection("Email"));
        services.AddHttpClient<EmailSender>();

        services.AddScoped<AuthService>();
        services.AddScoped<PostsService>();
        services.AddScoped<SocialService>();
        services.AddScoped<DiscoveryService>();
        services.AddScoped<DigestService>();
        services.AddScoped<SettingsService>();
        services.AddScoped<UploadsService>();
        services.AddScoped<AnalyticsService>();
        services.AddScoped<ViewTracker>();

        services.AddJobHandler<SendVerificationEmail, SendVerificationEmailHandler>();
        services.AddJobHandler<CommentNotification, CommentNotificationHandler>();
        services.AddJobHandler<LikeNotification, LikeNotificationHandler>();
        services.AddJobHandler<WeeklyDigestEmail, WeeklyDigestEmailHandler>();
        services.AddJobHandler<RefreshInterestProfile, RefreshInterestProfileHandler>();
        services.AddJobHandler<SendRecommendationDigest, SendRecommendationDigestHandler>();
        return builder;
    }
}
