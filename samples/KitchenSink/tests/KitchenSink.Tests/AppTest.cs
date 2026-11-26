using Inertia.Net.Testing;

namespace KitchenSink.Tests;

/// <summary>One signed-in browser per test, against an empty CRM dataset.</summary>
public abstract class AppTest(AppFactory app) : IAsyncLifetime
{
    protected AppFactory App { get; } = app;
    protected HttpClient Client { get; set; } = null!;

    public virtual async Task InitializeAsync()
    {
        await App.ResetCrmAsync();
        Client = await App.SignInAsync();
    }

    public virtual Task DisposeAsync() => Task.CompletedTask;

    protected Task<InertiaPage> PageAsync(string url, Action<HttpRequestMessage>? configure = null) =>
        App.PageAsync(Client, url, configure);

    protected Task<HttpResponseMessage> PostAsync(
        string url,
        object? body = null,
        HttpMethod? method = null,
        string? errorBag = null) =>
        App.PostAsync(Client, url, body, method, errorBag);

    protected static string LocationPath(HttpResponseMessage response)
    {
        var location = response.Headers.Location ?? throw new InvalidOperationException("The response has no Location header.");
        return location.IsAbsoluteUri ? location.AbsolutePath : location.OriginalString.Split('?')[0];
    }
}
