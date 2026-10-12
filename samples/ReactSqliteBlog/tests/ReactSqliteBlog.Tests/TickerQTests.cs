using ReactSqliteBlog.Cron;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using TickerQ.DependencyInjection;

namespace ReactSqliteBlog.Tests;

public class TickerQTests
{
    [Fact]
    public async Task Registered_cron_runs_through_TickerQ()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Configuration.AddInMemoryCollection(CronTasks.All.ToDictionary(
            task => $"Cron:{task.Name}", _ => (string?)"0 0 1 1 *"));
        builder.Services.AddCron(builder.Configuration);
        var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        builder.Services.MapTicker("scheduler-check", (_, _, _) =>
        {
            completed.TrySetResult();
            return Task.CompletedTask;
        }).WithCron("* * * * * *");
        using var host = builder.Build();
        host.UseTickerQ();
        await host.StartAsync();
        try { await completed.Task.WaitAsync(TimeSpan.FromSeconds(15)); }
        finally { await host.StopAsync(); }
    }
}
