using Inertia.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace TechLandier.Tests;

/// <summary>The web role points at a remote SSR pool and never starts a local Bun process.</summary>
public sealed class SsrConfigurationTests
{
    [Fact]
    public void MainApp_reads_ssr_urls_and_does_not_use_a_node_process()
    {
        using var app = new SsrFactory();
        // Touch Services so the host starts, as it would in a replica.
        _ = app.Services;

        var options = app.Services.GetRequiredService<IOptions<InertiaOptions>>().Value;
        Assert.Equal(SsrMode.Preferred, options.Ssr.Mode);
        Assert.Equal(new[] { "http://ssr-1:13714", "http://ssr-2:13714", "http://ssr-3:13714" }, options.Ssr.Urls);
        Assert.Null(options.Ssr.NodeProcess); // no UseNodeProcess
        Assert.Equal(TimeSpan.FromSeconds(2), options.Ssr.Timeout);
        Assert.Equal(2, options.Ssr.Concurrency.MaxActive);
        Assert.Equal(8, options.Ssr.Concurrency.MaxQueued);
        Assert.Equal(2, options.Ssr.CircuitBreaker.FailureThreshold);
        Assert.Equal(TimeSpan.FromSeconds(300), options.Ssr.Cache.Duration);
        Assert.Equal(TimeSpan.FromSeconds(60), options.Ssr.Cache.StaleFor);
    }

    sealed class SsrFactory : AppFactory
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.UseSetting("Inertia:Ssr", "true");
            builder.UseSetting("Inertia:SsrUrls:0", "http://ssr-1:13714");
            builder.UseSetting("Inertia:SsrUrls:1", "http://ssr-2:13714");
            builder.UseSetting("Inertia:SsrUrls:2", "http://ssr-3:13714");
            builder.UseSetting("Inertia:SsrTimeoutSeconds", "2");
            builder.UseSetting("Inertia:SsrMaxActive", "2");
            builder.UseSetting("Inertia:SsrMaxQueued", "8");
            builder.UseSetting("Inertia:SsrFailureThreshold", "2");
            builder.UseSetting("Inertia:SsrCacheSeconds", "300");
            builder.UseSetting("Inertia:SsrStaleSeconds", "60");
        }
    }
}
