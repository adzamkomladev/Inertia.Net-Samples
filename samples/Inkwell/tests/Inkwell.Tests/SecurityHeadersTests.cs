namespace Inkwell.Tests;

public class SecurityHeadersTests(BlogFactory f) : IClassFixture<BlogFactory>
{
    [Theory]
    [InlineData("/posts")]
    [InlineData("/login")]
    [InlineData("/email/unsubscribe?token=bad")]
    public async Task Every_response_carries_the_baseline_headers(string url)
    {
        using var r = await f.NewClient().Http.GetAsync(url);
        Assert.Equal("nosniff", r.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal("DENY", r.Headers.GetValues("X-Frame-Options").Single());
        Assert.Equal("strict-origin-when-cross-origin", r.Headers.GetValues("Referrer-Policy").Single());
        Assert.Contains("frame-ancestors 'none'", r.Headers.GetValues("Content-Security-Policy").Single());
    }
}
