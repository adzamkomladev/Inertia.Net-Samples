using ReactSqliteReplicasBlog.Infrastructure;
using Inertia.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace ReactSqliteReplicasBlog.Tests;

public class RequestAndRenderingTests
{
    [Fact]
    public async Task Private_html_skips_ssr_http_calls_while_public_feed_uses_ssr()
    {
        var handler = new SsrHandler();
        using var baseFactory = new BlogFactory();
        using var factory = baseFactory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        {
            services.PostConfigure<InertiaOptions>(o =>
            {
                o.Ssr.Enabled = true;
                o.Ssr.BundlePath = null;
                // Mock Vite SSR; managed SSR now requires a ready child process.
                o.Vite.DevServerUrl = "http://127.0.0.1:5173";
                o.Vite.HotFilePath = "missing-test-hot-file";
            });
            services.AddHttpClient("Inertia.Net.Ssr").ConfigurePrimaryHttpMessageHandler(() => handler);
        }));
        using var client = factory.CreateClient();
        foreach (var path in new[] { "/login", "/register" })
        {
            var html = await client.GetStringAsync(path);
            Assert.Contains("data-page=", html);
            Assert.DoesNotContain("ssr-test-marker", html);
            Assert.DoesNotContain("@vite(", html);
            Assert.DoesNotContain("@viteReactRefresh", html);
        }
        Assert.Equal(0, handler.Calls);
        Assert.Contains("ssr-test-marker", await client.GetStringAsync("/posts"));
        Assert.Equal(1, handler.Calls);
    }

    private sealed class SsrHandler : HttpMessageHandler
    {
        public int Calls { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Calls++;
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent("{\"head\":[],\"body\":\"<div>ssr-test-marker</div>\"}")
            });
        }
    }

    [Theory]
    [InlineData("Posts/Index", true)]
    [InlineData("Posts/Show", true)]
    [InlineData("Auth/Login", false)]
    [InlineData("Auth/Register", false)]
    [InlineData("Auth/VerifyEmail", false)]
    [InlineData("Analytics/Index", false)]
    [InlineData("Posts/Analytics", false)]
    [InlineData("Settings/Index", false)]
    [InlineData("Profile/Index", false)]
    [InlineData("Posts/Create", false)]
    [InlineData("Posts/Edit", false)]
    public void Only_public_post_pages_use_ssr(string component, bool expected) =>
        Assert.Equal(expected, PublicPostRootView.UsesSsr(component));

    [Theory]
    [InlineData(false, 404)]
    [InlineData(true, 500)]
    public async Task Request_log_includes_status_timing_memory_and_prefetch_without_query_secrets(bool fails, int status)
    {
        var log = new CaptureLog();
        var context = new DefaultHttpContext();
        context.Request.Method = "GET";
        context.Request.Path = "/missing";
        context.Request.QueryString = new QueryString("?token=secret");
        context.Request.Headers["X-Inertia-Prefetch"] = "true";
        var middleware = new RequestLogMiddleware(_ =>
        {
            if (fails) throw new InvalidOperationException("test failure");
            context.Response.StatusCode = 404;
            return Task.CompletedTask;
        }, log);
        if (fails) await Assert.ThrowsAsync<InvalidOperationException>(() => middleware.InvokeAsync(context));
        else await middleware.InvokeAsync(context);
        Assert.Equal(status, log.Values["StatusCode"]);
        Assert.Equal("/missing", log.Values["Path"]);
        Assert.Equal(true, log.Values["Prefetch"]);
        Assert.True((double)log.Values["WorkingSetMiB"]! > 0);
        Assert.True((double)log.Values["ManagedHeapMiB"]! > 0);
        Assert.True((double)log.Values["ElapsedMs"]! >= 0);
        Assert.DoesNotContain("secret", string.Join(" ", log.Values.Values));
    }

    private sealed class CaptureLog : ILogger<RequestLogMiddleware>
    {
        public Dictionary<string, object?> Values { get; private set; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel level) => true;
        public void Log<TState>(LogLevel level, EventId id, TState state, Exception? error, Func<TState, Exception?, string> format) =>
            Values = ((IEnumerable<KeyValuePair<string, object?>>)(object)state!).ToDictionary(x => x.Key, x => x.Value);
    }
}
