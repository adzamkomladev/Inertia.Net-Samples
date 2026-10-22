using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using TechLandier.Infrastructure;

namespace TechLandier.Tests;

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
    public void Task_returning_implementations_use_async_await()
    {
        var assemblies = new[] { typeof(Program).Assembly, typeof(FeatureBoundaryTests).Assembly };
        var forwarding = assemblies.SelectMany(a => a.GetTypes())
            .SelectMany(t => t.GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic
                | System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.DeclaredOnly))
            // Awaited base calls get compiler-generated, non-async forwarding bridges.
            .Where(m => !m.IsSpecialName && !m.Name.StartsWith("<>n__", StringComparison.Ordinal)
                && m.GetMethodBody() is not null && IsTask(m.ReturnType)
                && !m.IsDefined(typeof(System.Runtime.CompilerServices.AsyncStateMachineAttribute), false))
            .Select(m => $"{m.DeclaringType!.FullName}.{m.Name}").Order().ToArray();
        Assert.True(forwarding.Length == 0, "Await async work instead of forwarding tasks:\n" + string.Join("\n", forwarding));

        static bool IsTask(Type type) => typeof(Task).IsAssignableFrom(type) || type == typeof(ValueTask)
            || (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(ValueTask<>));
    }

    [Fact]
    public void Feature_services_return_data_instead_of_http_results()
    {
        var services = typeof(Program).Assembly.GetTypes()
            .Where(t => t.Namespace?.StartsWith("TechLandier.Features") == true && t.Name.EndsWith("Service"));
        Assert.NotEmpty(services);
        foreach (var type in services)
        foreach (var method in type.GetMethods())
            Assert.False(IsHttpResult(method.ReturnType), $"{type.Name}.{method.Name} returns an HTTP result.");

        static bool IsHttpResult(Type type) => typeof(IResult).IsAssignableFrom(type)
            || (type.IsGenericType && type.GenericTypeArguments.Any(IsHttpResult));
    }
}

