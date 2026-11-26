using System.Net;
using Inertia.Net.Testing;

namespace KitchenSink.Tests;

/// <summary>A smaller window keeps the limiter test quick while exercising the same middleware.</summary>
public sealed class RateLimitAppFactory : AppFactory
{
    protected override bool GlobalRateLimit => true;
    protected override int GlobalRateLimitPerMinute => 20;
    protected override int PartialRateLimit => 10;
}

public sealed class RateLimitTests
{
    [Fact]
    public async Task Page_requests_are_rate_limited()
    {
        await using var app = new RateLimitAppFactory();
        var client = await app.SignInAsync();

        HttpResponseMessage? last = null;
        for (var i = 0; i < 40; i++)
        {
            last?.Dispose();
            last = await client.InertiaGetAsync("/dashboard", AppFactory.Version);
            if (last.StatusCode == HttpStatusCode.TooManyRequests)
            {
                break;
            }
        }

        Assert.NotNull(last);
        Assert.Equal(HttpStatusCode.TooManyRequests, last.StatusCode);
        last.Dispose();
    }

    [Fact]
    public async Task Partial_reloads_have_their_own_higher_window()
    {
        await using var app = new RateLimitAppFactory();
        var client = await app.SignInAsync();

        HttpResponseMessage? last = null;
        for (var i = 0; i < 20; i++)
        {
            last?.Dispose();
            last = await client.InertiaGetAsync("/features/data-loading/polling", AppFactory.Version, request =>
            {
                request.Headers.TryAddWithoutValidation("X-Inertia-Partial-Component", "Features/DataLoading/Polling");
                request.Headers.TryAddWithoutValidation("X-Inertia-Partial-Data", "currentTime");
            });

            if (last.StatusCode == HttpStatusCode.TooManyRequests)
            {
                break;
            }
        }

        Assert.NotNull(last);
        Assert.Equal(HttpStatusCode.TooManyRequests, last.StatusCode);
        last.Dispose();
    }
}
