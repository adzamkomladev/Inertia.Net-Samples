#:sdk Aspire.AppHost.Sdk@13.6.1
#:package Aspire.Hosting.PostgreSQL@13.6.1
#:package Aspire.Hosting.Redis@13.6.1
#:package Aspire.Hosting.Yarp@13.6.1
#:package DotNetEnv@3.1.1
#:property AspireUseCliBundle=true
#:property UserSecretsId=5a3bcb49-559d-4fa7-8e56-3ca104ad19c6

using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Yarp;
using Microsoft.Extensions.Configuration;
using Yarp.ReverseProxy.Configuration;

DotNetEnv.Env.NoClobber().TraversePath().Load();
var builder = DistributedApplication.CreateBuilder(args);
// File-based runs have no launch profile; retain persisted parameters in every environment.
builder.Configuration.AddUserSecrets<Program>(optional: true).AddEnvironmentVariables();
var sampleRoot = builder.AppHostDirectory;
var port = builder.Configuration.GetValue("Gateway:Port", 8183);
var publicUrl = $"http://localhost:{port}";

var postgres = builder.AddPostgres("postgres")
    .WithImageTag("18")
    .WithDataVolume("vue-inertia-advanced-aspire-postgres");
var database = postgres.AddDatabase("blog", "vue_inertia_advanced");
var redis = builder.AddRedis("redis")
    .WithDataVolume("vue-inertia-advanced-aspire-redis");

IResourceBuilder<ProjectResource> Configure(IResourceBuilder<ProjectResource> project) => project
    .WithReference(database)
    .WithReference(redis)
    .WithEnvironment("DATABASE_URL", database.Resource.ConnectionStringExpression)
    .WithEnvironment("APP_BASE_URL", publicUrl)
    .WithEnvironment("DATA_DIR", Path.Combine(sampleRoot, "storage"))
    .WithEnvironment("MEILISEARCH_INDEX", "VuePgReplicasAdvBlog-aspire-posts")
    .WithEnvironment("Replicas__Namespace", "VuePgReplicasAdvBlog:aspire")
    .WithEnvironment("Database__MigrateOnStartup", "false")
    .WithEnvironment("Auth__SecureCookies", "false")
    .WithEnvironment("TRUSTED_PROXY_NETWORKS", "127.0.0.0/8,::1/128,10.0.0.0/8,172.16.0.0/12,192.168.0.0/16")
    .WaitFor(database)
    .WaitFor(redis);

var migrate = Configure(builder.AddProject("migrate", "apps/MainApp/MainApp.csproj", launchProfileName: null))
    .WithArgs("--migrate")
    .WithEnvironment("Replicas__Enabled", "false");

int Count(string role, int fallback)
{
    var count = builder.Configuration.GetValue($"Replicas:{role}", fallback);
    return count is >= 1 and <= 10 ? count : throw new InvalidOperationException($"Replicas:{role} must be between 1 and 10.");
}

// Explicit destinations let YARP maintain affinity to a process, rather than an opaque TCP proxy.
var apps = new List<EndpointReference>();
for (var i = 1; i <= Count("MainApp", 3); i++)
{
    var app = Configure(builder.AddProject($"app{i}", "apps/MainApp/MainApp.csproj", launchProfileName: null))
        .WithEnvironment("INSTANCE_ID", "app" + i)
        .WithHttpEndpoint(name: "http", isProxied: false)
        .WithEndpoint(name: "ssr", env: "Inertia__SsrPort", isProxied: false)
        .WithHttpHealthCheck("/health")
        .WaitForCompletion(migrate);
    apps.Add(app.GetEndpoint("http"));
}

for (var i = 1; i <= Count("Worker", 2); i++)
    Configure(builder.AddProject($"worker{i}", "apps/Worker/Worker.csproj", launchProfileName: null))
        .WaitForCompletion(migrate);

var realtimeHosts = new List<EndpointReference>();
for (var i = 1; i <= Count("Realtime", 2); i++)
{
    var realtime = Configure(builder.AddProject($"realtime{i}", "apps/Realtime/Realtime.csproj", launchProfileName: null))
        .WithEnvironment("INSTANCE_ID", "realtime" + i)
        .WithHttpEndpoint(name: "http", isProxied: false)
        .WithHttpHealthCheck("/health")
        .WaitForCompletion(migrate);
    realtimeHosts.Add(realtime.GetEndpoint("http"));
}

SessionAffinityConfig Affinity(string cookie) => new()
{
    Enabled = true, Policy = "HashCookie", FailurePolicy = "Redistribute", AffinityKeyName = cookie,
    Cookie = new SessionAffinityCookieConfig { HttpOnly = true, Path = "/", SameSite = Microsoft.AspNetCore.Http.SameSiteMode.Lax }
};

var gateway = builder.AddYarp("gateway")
    .WithHostPort(port)
    .WithConfiguration(yarp =>
    {
        var web = yarp.AddCluster("mainapp", apps.Cast<object>().ToArray())
            .WithLoadBalancingPolicy("RoundRobin")
            .WithSessionAffinityConfig(Affinity("REPLICA"));
        var realtime = yarp.AddCluster("realtime", realtimeHosts.Cast<object>().ToArray())
            .WithLoadBalancingPolicy("RoundRobin")
            .WithSessionAffinityConfig(Affinity("REALTIME_REPLICA"));
        yarp.AddRoute("/realtime/{**rest}", realtime);
        yarp.AddRoute(web);
    });

// Multi-destination clusters need explicit service discovery references.
foreach (var endpoint in apps.Concat(realtimeHosts)) gateway.WithReference(endpoint);

await builder.Build().RunAsync();

