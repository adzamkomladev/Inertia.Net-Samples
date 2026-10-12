using TickerQ.DependencyInjection;
using VuePgReplicasAdvBlog.Infrastructure;
using VuePgReplicasAdvBlog.Infrastructure.Setup;
using VuePgReplicasAdvBlog.Worker;

DotNetEnv.Env.NoClobber().TraversePath().Load();
var builder = Host.CreateApplicationBuilder(args);
builder.Configuration.ApplyPlainNames();
builder.AddServiceDefaults();
builder.AddCommonData().AddCommonInfrastructure().AddWorkerServices();

using var host = builder.Build();
if (builder.Configuration.GetValue("Cron:Enabled", true)) host.UseTickerQ();
await host.RunAsync();
