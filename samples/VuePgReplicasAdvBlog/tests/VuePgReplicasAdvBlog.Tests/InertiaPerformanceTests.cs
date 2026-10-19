using System.Diagnostics;
using System.Text.Json;
using Inertia.Net.Testing;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;

namespace VuePgReplicasAdvBlog.Tests;

public class InertiaPerformanceTests
{
    [Fact]
    public async Task Measure_rendering_when_requested()
    {
        var output = Environment.GetEnvironmentVariable("INERTIA_BENCHMARK_OUTPUT");
        if (string.IsNullOrEmpty(output)) return;
        using var app = new AppFactory();
        using var quiet = app.WithWebHostBuilder(builder =>
        {
            builder.ConfigureLogging(logging => logging.ClearProviders());
            builder.ConfigureServices(services => services.PostConfigure<InertiaOptions>(o => o.Ssr.Mode = SsrMode.Disabled));
        });
        using var client = quiet.CreateClient(new() { AllowAutoRedirect = false, BaseAddress = new Uri("https://localhost") });
        using var initial = await client.GetAsync("/posts");
        initial.EnsureSuccessStatusCode();
        var page = await initial.GetInertiaPageAsync();
        var results = new List<object>();
        foreach (var mode in new[] { "html", "json", "partial" })
        {
            client.DefaultRequestHeaders.Clear();
            if (mode != "html")
            {
                client.DefaultRequestHeaders.Add("X-Inertia", "true");
                client.DefaultRequestHeaders.Add("X-Inertia-Version", page.Version ?? "");
            }
            if (mode == "partial")
            {
                client.DefaultRequestHeaders.Add("X-Inertia-Partial-Component", page.Component);
                client.DefaultRequestHeaders.Add("X-Inertia-Partial-Data", "posts");
            }
            // shortcut: empty feed, one client and fixture SSR settings; use seeded k6 runs for production capacity.
            for (var i = 0; i < 100; i++) await Request();
            for (var round = 0; round < 5; round++)
            {
                var latencies = new double[500];
                var allocated = GC.GetTotalAllocatedBytes(true);
                var total = Stopwatch.StartNew();
                for (var i = 0; i < latencies.Length; i++)
                {
                    var start = Stopwatch.GetTimestamp();
                    await Request();
                    latencies[i] = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
                }
                total.Stop();
                var bytes = GC.GetTotalAllocatedBytes(true) - allocated;
                Array.Sort(latencies);
                results.Add(new { mode, round, requests = latencies.Length, meanMs = total.Elapsed.TotalMilliseconds / latencies.Length,
                    p50Ms = latencies[249], p95Ms = latencies[474], requestsPerSecond = latencies.Length / total.Elapsed.TotalSeconds,
                    allocatedBytesPerRequest = bytes / latencies.Length });
            }
        }
        await File.WriteAllTextAsync(output, JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true }));

        async Task Request()
        {
            using var response = await client.GetAsync("/posts");
            Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
            var body = await response.Content.ReadAsStringAsync();
            if (modeIsJson())
            {
                using var json = JsonDocument.Parse(body);
                Assert.Equal(page.Component, json.RootElement.GetProperty("component").GetString());
                Assert.True(json.RootElement.GetProperty("props").TryGetProperty("posts", out _));
            }
            else Assert.Contains("data-page=", body);
        }
        bool modeIsJson() => client.DefaultRequestHeaders.Contains("X-Inertia");
    }
}
