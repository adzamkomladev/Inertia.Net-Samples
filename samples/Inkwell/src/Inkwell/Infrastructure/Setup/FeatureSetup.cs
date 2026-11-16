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
using Microsoft.AspNetCore.Identity;
using System.Text.Json.Serialization.Metadata;

namespace Inkwell.Infrastructure.Setup;

/// <summary>Services, background jobs, cron and storage.</summary>
public static class FeatureSetup
{
    public static WebApplicationBuilder AddBlogFeatures(this WebApplicationBuilder builder)
    {
        var services = builder.Services;
        var config = builder.Configuration;

        services.AddScoped<Inkwell.Features.Team.TeamService>();
        services.AddSignalR().AddJsonProtocol(o =>
        {
            // The live-props transport (InertiaSetup) pins its source-generated context into the hub payload resolver
            // chain. Keep a reflection resolver behind it so this app's own hubs keep sending typed payloads.
            var chain = o.PayloadSerializerOptions.TypeInfoResolverChain;
            if (!chain.OfType<DefaultJsonTypeInfoResolver>().Any()) chain.Add(new DefaultJsonTypeInfoResolver());
        });
        services.AddSingleton<RealtimeEvents>();
        services.AddSingleton<RealtimeDelivery>();
        // Background work runs on Runwick (RunwickSetup registers the jobs and handlers).
        if (RunwickSetup.IsEnabled(config)) services.AddSingleton<IJobOutbox, RunwickJobOutbox>();
        else services.AddSingleton<IJobOutbox, DisabledJobOutbox>();
        services.AddStorage(config);

        // Registered after AddIdentityCore, so this factory wins.
        services.AddScoped<IUserClaimsPrincipalFactory<AppUser>, AppClaimsPrincipalFactory>();
        services.Configure<EmailOptions>(config.GetSection("Email"));
        services.AddHttpClient<EmailSender>();

        services.AddScoped<Inkwell.Features.Notifications.NotificationsService>();
        services.AddScoped<AuthService>();
        services.AddScoped<PostsService>();
        services.AddScoped<SocialService>();
        services.AddScoped<DiscoveryService>();
        services.AddScoped<DigestService>();
        services.AddScoped<EmailUnsubscribe>();
        services.AddScoped<SettingsService>();
        services.AddScoped<UploadsService>();
        services.AddScoped<AnalyticsService>();
        services.AddScoped<ViewTracker>();

        return builder;
    }
}
