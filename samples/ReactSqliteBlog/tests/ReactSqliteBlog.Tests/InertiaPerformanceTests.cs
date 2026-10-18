using System.Diagnostics;
using System.Text.Json;
using Inertia.Net.Testing;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;

namespace ReactSqliteBlog.Tests;

public class InertiaPerformanceTests
{
    // A ~24 KB rendered feed with non-ASCII text and escapes, so the SSR mode measures the adapter's response parsing.
    static readonly byte[] SsrResponse = JsonSerializer.SerializeToUtf8Bytes(new
    {
        head = Enumerable.Range(0, 8).Select(i => $"<meta name=\"t{i}\" content=\"Café «{i}» — ünïcödé\">").ToArray(),
        body = "<div id=\"app\">" + string.Concat(Enumerable.Range(0, 60).Select(i =>
            $"<article class=\"post\"><h2>Post {i}: naïve résumé ✓</h2><p>Line \"one\" & <b>two</b> — {new string('x', 220)}</p></article>")) + "</div>",
    });

    [Fact]
    public async Task Measure_rendering_when_requested()
    {
        var output = Environment.GetEnvironmentVariable("INERTIA_BENCHMARK_OUTPUT");
        if (string.IsNullOrEmpty(output)) return;
        using var app = new BlogFactory();
        var results = new List<object>();
        InertiaPage? page = null; // parsed from the first, client-rendered mode: an SSR body replaces <script data-page>
        foreach (var mode in new[] { "html", "json", "partial", "ssr" })
        {
            using var quiet = app.WithWebHostBuilder(builder =>
            {
                builder.ConfigureLogging(logging => logging.ClearProviders());
                builder.ConfigureServices(services =>
                {
                    services.PostConfigure<InertiaOptions>(o => { o.Ssr.Enabled = mode == "ssr"; o.Ssr.BundlePath = null; });
                    services.AddHttpClient("Inertia.Net.Ssr").ConfigurePrimaryHttpMessageHandler(() => new SsrHandler());
                });
            });
            using var client = quiet.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, BaseAddress = new Uri("https://localhost") });
            using var initial = await client.GetAsync("/posts");
            initial.EnsureSuccessStatusCode();
            page ??= await initial.GetInertiaPageAsync();
            var json = mode is "json" or "partial";
            if (json)
            {
                client.DefaultRequestHeaders.Add("X-Inertia", "true");
                client.DefaultRequestHeaders.Add("X-Inertia-Version", page.Version ?? "");
            }
            if (mode == "partial")
            {
                client.DefaultRequestHeaders.Add("X-Inertia-Partial-Component", page.Component);
                client.DefaultRequestHeaders.Add("X-Inertia-Partial-Data", "posts");
            }
            // shortcut: empty feed, one client and a canned SSR reply; use seeded k6 runs for production capacity.
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

            async Task Request()
            {
                using var response = await client.GetAsync("/posts");
                Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
                var body = await response.Content.ReadAsStringAsync();
                if (json)
                {
                    using var doc = JsonDocument.Parse(body);
                    Assert.Equal(page.Component, doc.RootElement.GetProperty("component").GetString());
                    Assert.True(doc.RootElement.GetProperty("props").TryGetProperty("posts", out _));
                }
                else if (mode == "ssr") Assert.Contains("naïve résumé", body);
                else Assert.Contains("data-page=", body);
            }
        }
        await File.WriteAllTextAsync(output, JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true }));
    }

    sealed class SsrHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(SsrResponse) { Headers = { ContentType = new("application/json") { CharSet = "utf-8" } } },
            });
    }
}
