using TickerQ.DependencyInjection;
using TechLandier.Infrastructure;
using TechLandier.Worker;

DotNetEnv.Env.NoClobber().TraversePath().Load();
var builder = Host.CreateApplicationBuilder(args);
builder.Configuration.ApplyPlainNames();
builder.AddWorkerServices();

using var host = builder.Build();
if (builder.Configuration.GetValue("Cron:Enabled", true)) host.UseTickerQ();
await host.RunAsync();

/// <summary>Stable entry-point marker for host-build tests.</summary>
public sealed class WorkerHostMarker;
