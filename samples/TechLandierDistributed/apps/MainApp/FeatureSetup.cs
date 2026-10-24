using Microsoft.Extensions.Http.Resilience;
using TechLandier.Infrastructure;

namespace TechLandier.Infrastructure.Setup;

public static class FeatureSetup
{
    public static WebApplicationBuilder AddStoreFeatures(this WebApplicationBuilder builder)
    {
        var services = builder.Services;
        services.AddCoreServices();
        services.AddSingleton<ViewTracker>();
        services.AddHostedService<ViewEventWriter>();
        // The job worker already retries; a retried POST could double-send an email.
        services.AddHttpClient<Mailer>().AddStandardResilienceHandler(o => o.Retry.DisableFor(HttpMethod.Post));
        return builder;
    }
}
