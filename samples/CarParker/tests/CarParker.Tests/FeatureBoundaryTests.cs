using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using CarParker.Infrastructure;

namespace CarParker.Tests;

public class FeatureBoundaryTests
{
    [Fact]
    public async Task Disabled_workers_do_not_access_the_database()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Jobs:Enabled"] = "false"
        }).Build();
        using var worker = new JobWorker(null!, new JobQueue(), new JobRegistry(),
            TimeProvider.System, config, NullLogger<JobWorker>.Instance);
        await worker.StartAsync(CancellationToken.None);
        await worker.ExecuteTask!.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.True(worker.ExecuteTask.IsCompletedSuccessfully);
    }

    [Fact]
    public void Feature_services_return_data_instead_of_http_results()
    {
        var services = typeof(Program).Assembly.GetTypes()
            .Where(t => t.Namespace?.StartsWith("CarParker.Features") == true && t.Name.EndsWith("Service"));
        Assert.NotEmpty(services);
        foreach (var type in services)
        foreach (var method in type.GetMethods())
            Assert.False(IsHttpResult(method.ReturnType), $"{type.Name}.{method.Name} returns an HTTP result.");

        static bool IsHttpResult(Type type) => typeof(IResult).IsAssignableFrom(type)
            || (type.IsGenericType && type.GenericTypeArguments.Any(IsHttpResult));
    }
}

