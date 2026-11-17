using System.Net;
using MongoDB.Driver;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;
using CarParker.Data;
using CarParker.Features;

namespace CarParker.Tests;

public sealed class PasswordResetTests(AppFactory app) : IClassFixture<AppFactory>
{
    async Task<User> UserAsync(Guid id) => await app.QueryAsync(async db => await db.Users.Find(u => u.Id == id).SingleAsync());

    string TokenFor(User user)
    {
        var url = AuthService.ResetUrl(app.Services.GetRequiredService<IDataProtectionProvider>(), "http://test", user);
        return Uri.UnescapeDataString(url[(url.IndexOf("token=", StringComparison.Ordinal) + 6)..]);
    }

    [Fact]
    public async Task Requests_answer_the_same_way_and_only_real_accounts_get_a_mail()
    {
        var email = $"reset-{Guid.NewGuid():N}@example.test";
        var (_, userId) = await app.SignUpAndVerifyAsync(email);

        using var known = await app.SendAsync(app.Client(), "/forgot-password", new { email });
        using var unknown = await app.SendAsync(app.Client(), "/forgot-password", new { email = $"nobody-{Guid.NewGuid():N}@example.test" });
        Assert.Equal(HttpStatusCode.Redirect, known.StatusCode);
        Assert.Equal(unknown.StatusCode, known.StatusCode);
        Assert.Equal(unknown.Headers.Location, known.Headers.Location);

        // Only the registered address queues a job; the payload is the user id, never the address or a token.
        Assert.Equal("succeeded", await app.WaitForJobAsync("mail.password-reset", userId.ToString()));
        Assert.DoesNotContain(await app.RunwickPayloadsAsync("mail.password-reset"), p => p.Contains(email) || p.Contains("token"));
    }

    [Fact]
    public async Task Demo_accounts_never_get_a_reset_link()
    {
        var email = $"demo-reset-{Guid.NewGuid():N}@example.test";
        var (_, userId) = await app.SignUpAndVerifyAsync(email);
        await app.QueryAsync(async db => await db.Users.UpdateOneAsync(u => u.Id == userId, Builders<User>.Update.Set(u => u.IsDemo, true)));

        using var response = await app.SendAsync(app.Client(), "/forgot-password", new { email });
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Null(await app.RunwickJobStateAsync("mail.password-reset", userId.ToString()));
        // A token minted anyway is refused.
        using var page = await app.Client().GetAsync($"/reset-password?token={Uri.EscapeDataString(TokenFor(await UserAsync(userId)))}");
        Assert.Equal("/forgot-password", page.Headers.Location?.OriginalString);
    }

    [Fact]
    public async Task A_reset_changes_the_password_once_and_signs_out_other_sessions()
    {
        var email = $"reset-flow-{Guid.NewGuid():N}@example.test";
        var (signedIn, userId) = await app.SignUpAndVerifyAsync(email);
        using (var before = await app.InertiaGetAsync(signedIn, "/account")) Assert.Equal(HttpStatusCode.OK, before.StatusCode);
        var token = TokenFor(await UserAsync(userId));

        using var form = await app.Client().GetAsync($"/reset-password?token={Uri.EscapeDataString(token)}");
        Assert.Equal(HttpStatusCode.OK, form.StatusCode);

        // Mismatched confirmation is a validation error, nothing changes.
        using var mismatch = await app.SendAsync(app.Client(), "/reset-password", new { token, password = "brand-new-pass-1", passwordConfirmation = "other-pass-123" });
        Assert.Null((await UserAsync(userId)).SessionStamp);

        using var reset = await app.SendAsync(app.Client(), "/reset-password", new { token, password = "brand-new-pass-1", passwordConfirmation = "brand-new-pass-1" });
        Assert.Equal(HttpStatusCode.Redirect, reset.StatusCode);
        Assert.Equal("/login", reset.Headers.Location?.OriginalString);
        Assert.NotNull((await UserAsync(userId)).SessionStamp);

        // The old cookie no longer works.
        using (var after = await app.InertiaGetAsync(signedIn, "/account")) Assert.NotEqual(HttpStatusCode.OK, after.StatusCode);

        // The new password logs in, the old one does not.
        using var oldLogin = await app.SendAsync(app.Client(), "/login", new { email, password = AppFactory.Password });
        Assert.NotEqual("/lots", oldLogin.Headers.Location?.OriginalString);
        var fresh = app.Client();
        using var newLogin = await app.SendAsync(fresh, "/login", new { email, password = "brand-new-pass-1" });
        Assert.Equal("/lots", newLogin.Headers.Location?.OriginalString);
        using (var account = await app.InertiaGetAsync(fresh, "/account")) Assert.Equal(HttpStatusCode.OK, account.StatusCode);

        // The link is spent.
        using var reuse = await app.SendAsync(app.Client(), "/reset-password", new { token, password = "another-pass-22", passwordConfirmation = "another-pass-22" });
        Assert.Equal("/forgot-password", reuse.Headers.Location?.OriginalString);
    }
}
