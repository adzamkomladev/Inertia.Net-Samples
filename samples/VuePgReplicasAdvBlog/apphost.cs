#:sdk Aspire.AppHost.Sdk@13.6.1
#:package Aspire.Hosting.PostgreSQL@13.6.1
#:package Aspire.Hosting.Redis@13.6.1
#:package Aspire.Hosting.Yarp@13.6.1
#:package Aspire.Hosting.Docker@13.6.1
#:package DotNetEnv@3.1.1
#:property AspireUseCliBundle=true
#:property UserSecretsId=vuepgreplicasadvblog-apphost

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
var publishing = builder.ExecutionContext.IsPublishMode;
builder.AddDockerComposeEnvironment("compose")
    .WithDashboard(false)
    .ConfigureComposeFile(file =>
    {
        foreach (var service in file.Services.Values)
        {
            if (service.Name is not ("migrate" or "seed")) service.Restart = "unless-stopped";
            foreach (var dependency in service.DependsOn.Where(item => item.Key is "postgres" or "redis"))
                dependency.Value.Condition = "service_healthy";
        }
    });
var publicUrl = publishing
    ? ReferenceExpression.Create($"{builder.AddParameter("app-base-url")}")
    : ReferenceExpression.Create($"http://localhost:{port.ToString(System.Globalization.CultureInfo.InvariantCulture)}");
var environment = publishing ? "production" : "aspire";
var deploymentSettings = new (string Key, bool Secret)[]
{
    ("EMAIL_API_KEY", true), ("EMAIL_FROM_ADDRESS", false), ("EMAIL_FROM_NAME", false),
    ("S3_ENDPOINT", false), ("S3_REGION", false), ("S3_BUCKET", false), ("S3_BACKUP_BUCKET", false),
    ("S3_ACCESS_KEY", true), ("S3_SECRET_KEY", true), ("S3_PUBLIC_URL", false),
    ("S3_PUBLIC_ENDPOINT", false), ("ANALYTICS_SALT", true),
    ("SEARCH_DRIVER", false), ("MEILISEARCH_URL", false), ("MEILISEARCH_API_KEY", true)
}.Select(setting => (setting.Key, Parameter: publishing
    ? builder.AddParameter(setting.Key.ToLowerInvariant().Replace('_', '-'),
        () => builder.Configuration[setting.Key] ?? "", secret: setting.Secret)
    : null)).ToArray();

var postgres = builder.AddPostgres("postgres")
    .WithImageTag("18")
    .WithEnvironment("POSTGRES_DB", "vue_pg_replicas_adv_blog")
    .PublishAsDockerComposeService((_, service) => service.Healthcheck = new()
    {
        Test = ["CMD-SHELL", "pg_isready -h 127.0.0.1 -U $$POSTGRES_USER -d $$POSTGRES_DB"],
        Interval = "5s", Timeout = "5s", Retries = 20, StartPeriod = "20s"
    })
    .WithDataVolume("vuepgreplicasadvblog-aspire-postgres");
var database = postgres.AddDatabase("blog", "vue_pg_replicas_adv_blog");
var redis = builder.AddRedis("redis")
    .WithDataVolume("vuepgreplicasadvblog-aspire-redis")
    .WithArgs("--appendonly", "yes")
    .PublishAsDockerComposeService((_, service) =>
    {
        // Redis's publish adapter runs a shell; append options inside its command, not after sh -c.
        service.Command = ["-c", "exec redis-server --requirepass \"$$REDIS_PASSWORD\" --appendonly yes"];
        service.Healthcheck = new()
        {
            Test = ["CMD-SHELL", "REDISCLI_AUTH=\"$$REDIS_PASSWORD\" redis-cli ping"],
            Interval = "5s", Timeout = "3s", Retries = 20, StartPeriod = "10s"
        };
    });

IResourceBuilder<ProjectResource> Configure(IResourceBuilder<ProjectResource> project)
{
    project
    .WithReference(database)
    .WithReference(redis)
    .WithEnvironment("DATABASE_URL", database.Resource.ConnectionStringExpression)
    .WithEnvironment("APP_BASE_URL", publicUrl)
    .WithEnvironment("App__BaseUrl", publicUrl)
    .WithEnvironment("DATA_DIR", publishing ? "/data" : Path.Combine(sampleRoot, "storage"))
    .WithEnvironment("MEILISEARCH_INDEX", $"VuePgReplicasAdvBlog-{environment}-posts")
    .WithEnvironment("Replicas__Namespace", $"VuePgReplicasAdvBlog:{environment}")
    .WithEnvironment("TickerQ__RedisDatabase", builder.Configuration["TickerQ:RedisDatabase"] ?? "1")
    .WithEnvironment("Database__MigrateOnStartup", "false")
    .WithEnvironment("Auth__SecureCookies", publishing ? "true" : "false")
    .WithEnvironment("TRUSTED_PROXY_NETWORKS", "127.0.0.0/8,::1/128,10.0.0.0/8,172.16.0.0/12,192.168.0.0/16")
    .WaitFor(database)
    .WaitFor(redis);
    foreach (var (key, parameter) in deploymentSettings)
        if (parameter is not null) project.WithEnvironment(key, parameter);
    return project;
}

IResourceBuilder<ProjectResource> Containerize(IResourceBuilder<ProjectResource> project, string stage) =>
    project.PublishAsDockerFile(container =>
    {
        container.WithDockerfile(sampleRoot, stage: stage);
        if (stage == "worker") container.WithVolume("worker-data", "/data");
    });

var migrate = Containerize(Configure(builder.AddProject("migrate", "apps/MainApp/MainApp.csproj", launchProfileName: null)), "mainapp")
    .WithArgs("--migrate")
    .WithEnvironment("Replicas__Enabled", "false");

IResourceBuilder<ProjectResource> initialized = migrate;
if (builder.Configuration.GetValue("Seed:Demo", false))
    initialized = Containerize(Configure(builder.AddProject("seed", "apps/MainApp/MainApp.csproj", launchProfileName: null)), "mainapp")
        .WithArgs("--seed-demo")
        .WithEnvironment("Replicas__Enabled", "false")
        .WithEnvironment("Seed__Users", builder.Configuration["Seed:Users"] ?? "200")
        .WithEnvironment("Seed__Posts", builder.Configuration["Seed:Posts"] ?? "1000")
        .WaitForCompletion(migrate);

int Count(string role, int fallback)
{
    var count = builder.Configuration.GetValue($"Replicas:{role}", fallback);
    return count is >= 1 and <= 10 ? count : throw new InvalidOperationException($"Replicas:{role} must be between 1 and 10.");
}

// Explicit destinations let YARP maintain affinity to a process, rather than an opaque TCP proxy.
var apps = new List<EndpointReference>();
for (var i = 1; i <= Count("MainApp", 3); i++)
{
    var app = Containerize(Configure(builder.AddProject($"app{i}", "apps/MainApp/MainApp.csproj", launchProfileName: null)), "mainapp")
        .WithEnvironment("INSTANCE_ID", "app" + i)
        .WithHttpEndpoint(name: "http", targetPort: publishing ? 8080 : null, isProxied: false)
        .WithUrlForEndpoint("http", url => url.DisplayLocation = UrlDisplayLocation.DetailsOnly)
        .WithUrl(publicUrl, "Open blog via YARP")
        .WithHttpHealthCheck("/health")
        .WaitForCompletion(initialized);
    if (!publishing) app.WithEndpoint(name: "ssr", env: "Inertia__SsrPort", isProxied: false);
    apps.Add(app.GetEndpoint("http"));
}

for (var i = 1; i <= Count("Worker", 2); i++)
    Containerize(Configure(builder.AddProject($"worker{i}", "apps/Worker/Worker.csproj", launchProfileName: null)), "worker")
        .WithEnvironment("INSTANCE_ID", "worker" + i)
        .WaitForCompletion(initialized);

var realtimeHosts = new List<EndpointReference>();
for (var i = 1; i <= Count("Realtime", 2); i++)
{
    var realtime = Containerize(Configure(builder.AddProject($"realtime{i}", "apps/Realtime/Realtime.csproj", launchProfileName: null)), "realtime")
        .WithEnvironment("INSTANCE_ID", "realtime" + i)
        .WithHttpEndpoint(name: "http", targetPort: publishing ? 8080 : null, isProxied: false)
        .WithUrlForEndpoint("http", url => url.DisplayLocation = UrlDisplayLocation.DetailsOnly)
        .WithHttpHealthCheck("/health")
        .WaitForCompletion(initialized);
    realtimeHosts.Add(realtime.GetEndpoint("http"));
}

SessionAffinityConfig Affinity(string cookie) => new()
{
    Enabled = true, Policy = "HashCookie", FailurePolicy = "Redistribute", AffinityKeyName = cookie,
    Cookie = new SessionAffinityCookieConfig { HttpOnly = true, Path = "/", SameSite = Microsoft.AspNetCore.Http.SameSiteMode.Lax }
};

var gateway = builder.AddYarp("gateway")
    .WithHostPort(port)
    .WithExternalHttpEndpoints()
    .WithUrlForEndpoint("http", url => url.DisplayText = "Open blog (YARP)")
    .PublishAsDockerComposeService((_, service) =>
    {
        service.Ports.Clear();
        service.Expose.Add("5000");
    })
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

