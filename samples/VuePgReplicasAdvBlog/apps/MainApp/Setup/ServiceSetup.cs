using VuePgReplicasAdvBlog.Features.Analytics;
using VuePgReplicasAdvBlog.Features.Digests;
using VuePgReplicasAdvBlog.Features.Discovery;
using VuePgReplicasAdvBlog.Features.Search;
using VuePgReplicasAdvBlog.Features.Uploads;
using VuePgReplicasAdvBlog.Infrastructure;

namespace VuePgReplicasAdvBlog.MainApp.Setup;

public static class ServiceSetup
{
    public static WebApplicationBuilder AddMainAppServices(this WebApplicationBuilder builder)
    {
        builder.Services.AddValidation();
        builder.Services.AddScoped<DiscoveryService>();
        builder.Services.AddSingleton<DigestClock>();
        builder.Services.AddScoped<DigestService>();
        builder.Services.AddSingleton<Storage>();
        builder.Services.AddScoped<ViewTracker>();
        if (MeilisearchConnectionSettings.FromConfiguration(builder.Configuration) is { } meilisearch)
        {
            builder.Services.AddSingleton(meilisearch);
            builder.Services.AddHttpClient<MeilisearchTaskClient>(http => http.Timeout = TimeSpan.FromSeconds(30));
        }
        return builder;
    }
}
