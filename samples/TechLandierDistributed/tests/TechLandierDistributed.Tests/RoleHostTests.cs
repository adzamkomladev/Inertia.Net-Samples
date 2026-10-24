using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using TechLandier.Infrastructure;
using TechLandier.Worker;

namespace TechLandier.Tests;

/// <summary>Each role must build and resolve its own dependency set; only MainApp renders Inertia.</summary>
public sealed class RoleHostTests(AppFactory app) : IClassFixture<AppFactory>
{
    [Fact]
    public async Task Realtime_host_serves_health_and_negotiates_only_realtime()
    {
        _ = app.Services; // MainApp applies the fixture migrations before Realtime/Worker start.
        using var realtime = new RealtimeFactory(app.ConnectionString, app.DataDirectory);
        using var client = realtime.CreateClient();

        using var health = await client.GetAsync("/health");
        Assert.Equal(System.Net.HttpStatusCode.OK, health.StatusCode);
        using var alive = await client.GetAsync("/alive");
        Assert.Equal(System.Net.HttpStatusCode.OK, alive.StatusCode);

        // POST /realtime/negotiate exists. The origin middleware rejects an unsafe request without an Origin.
        using var denied = new HttpRequestMessage(HttpMethod.Post, "/realtime/negotiate?negotiateVersion=1");
        using var deniedResponse = await client.SendAsync(denied);
        Assert.Equal(System.Net.HttpStatusCode.Forbidden, deniedResponse.StatusCode);

        using var negotiate = new HttpRequestMessage(HttpMethod.Post, "/realtime/negotiate?negotiateVersion=1");
        negotiate.Headers.Add("Origin", "http://localhost");
        using var accepted = await client.SendAsync(negotiate);
        Assert.Equal(System.Net.HttpStatusCode.OK, accepted.StatusCode);
        Assert.Contains("connectionToken", await accepted.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Worker_host_builds_with_signalr_context_and_invalidates_without_inertia()
    {
        _ = app.Services;
        var builder = Host.CreateApplicationBuilder();
        builder.Environment.EnvironmentName = "Development";
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["DATABASE_URL"] = app.ConnectionString,
            ["App:DataDir"] = app.DataDirectory,
            ["App:BaseUrl"] = "http://localhost",
            ["Replicas:Enabled"] = "false",
            ["Redis:Url"] = "",
            ["Cache:RedisUrl"] = "",
            ["Jobs:Enabled"] = "false",
            ["Cron:Enabled"] = "false",
            ["EMAIL_API_KEY"] = "",
        });
        builder.AddWorkerServices();
        using var host = builder.Build();

        Assert.NotNull(host.Services.GetRequiredService<Microsoft.AspNetCore.SignalR.IHubContext<StoreHub>>());
        Assert.IsType<TechLandier.Caching.NoOpPropCacheInvalidator>(host.Services.GetRequiredService<IPropCacheInvalidator>());
        await host.StartAsync();
        await host.StopAsync();
    }

    sealed class RealtimeFactory(string connection, string dataDirectory) : WebApplicationFactory<RealtimeHostMarker>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.UseSetting("DATABASE_URL", connection);
            builder.UseSetting("App:DataDir", dataDirectory);
            builder.UseSetting("App:BaseUrl", "http://localhost");
            builder.UseSetting("Replicas:Enabled", "false");
            builder.UseSetting("Redis:Url", "");
            builder.UseSetting("Auth:SecureCookies", "false");
        }
    }
}
