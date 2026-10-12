using System.Buffers.Text;
using System.Net;
using System.Text;
using Inertia.Net.Testing;
using ReactSqliteReplicasBlog.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ReactSqliteReplicasBlog.Tests;

public class AuthTests(BlogFactory f) : IClassFixture<BlogFactory>
{
    private static bool IsRedirect(HttpResponseMessage r) => r.StatusCode is HttpStatusCode.Redirect or HttpStatusCode.SeeOther;

    [Fact]
    public async Task Register_invalid_data_returns_errors()
    {
        var c = f.NewClient();
        using var r = await c.PostAsync("/register", new { name = "A", email = "not-an-email", password = "short", passwordConfirmation = "short" });
        Assert.True(IsRedirect(r), r.StatusCode.ToString());
        using var next = await c.GetAsync(r.Headers.Location!.OriginalString);
        await next.AssertInertiaAsync(p => p.Has("errors", e => e.Has("email").Has("password").Etc()));
    }

    [Fact]
    public async Task Register_valid_signs_in_and_enqueues_verification_email()
    {
        var c = f.NewClient();
        var email = BlogFactory.NewEmail();
        using var r = await c.PostAsync("/register", new { name = "Ann", email, password = "Passw0rd!x", passwordConfirmation = "Passw0rd!x" });
        Assert.True(IsRedirect(r));
        Assert.Equal("/email/verify", r.Headers.Location!.OriginalString);

        var id = f.WithDb(db => db.Users.Single(u => u.Email == email).Id);
        Assert.True(f.WithDb(db => db.Jobs.Any(j => j.Type == "SendVerificationEmail" && j.Payload.Contains(id.ToString()))));

        using var page = await c.GetAsync("/email/verify");
        await page.AssertInertiaAsync(p => p.Component("Auth/VerifyEmail")
            .Where("auth.user.email", email).Where("auth.user.verified", false));
    }

    [Fact]
    public async Task Login_with_wrong_password_errors_on_email()
    {
        var email = BlogFactory.NewEmail();
        await f.CreateUserAsync(email);
        var c = f.NewClient();
        using var r = await c.LoginAsync(email, "wrong-password");
        Assert.True(IsRedirect(r));
        using var next = await c.GetAsync(r.Headers.Location!.OriginalString);
        await next.AssertInertiaAsync(p => p.Has("errors.email").WhereNull("auth.user"));
    }

    [Fact]
    public async Task Logout_clears_auth_user()
    {
        var email = BlogFactory.NewEmail();
        await f.CreateUserAsync(email);
        var c = f.NewClient();
        await c.LoginOkAsync(email);
        using (var me = await c.GetAsync("/posts")) await me.AssertInertiaAsync(p => p.Where("auth.user.email", email));

        using var r = await c.PostAsync("/logout");
        Assert.Equal("/login", r.Headers.Location!.OriginalString);
        using var after = await c.GetAsync("/posts");
        await after.AssertInertiaAsync(p => p.WhereNull("auth.user"));
    }

    [Theory]
    [InlineData("//evil.com")]
    [InlineData("/\\evil.com")]
    [InlineData("https://evil.com")]
    [InlineData("/\t/evil.com")] // browsers strip the tab and treat it as //evil.com
    public async Task Login_ignores_non_local_return_url(string returnUrl)
    {
        var email = BlogFactory.NewEmail();
        await f.CreateUserAsync(email);
        var c = f.NewClient();
        using var r = await c.LoginAsync(email, url: "/login?returnUrl=" + Uri.EscapeDataString(returnUrl));
        Assert.Equal("/posts", r.Headers.Location!.OriginalString);
    }

    [Fact]
    public async Task Login_follows_local_return_url()
    {
        var email = BlogFactory.NewEmail();
        await f.CreateUserAsync(email);
        var c = f.NewClient();
        using var r = await c.LoginAsync(email, url: "/login?returnUrl=%2Fanalytics");
        Assert.Equal("/analytics", r.Headers.Location!.OriginalString);
    }

    private async Task<(Guid Id, string Token)> TokenFor(string email)
    {
        using var scope = f.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        var user = (await users.FindByEmailAsync(email))!;
        var token = await users.GenerateEmailConfirmationTokenAsync(user);
        return (user.Id, Base64Url.EncodeToString(Encoding.UTF8.GetBytes(token)));
    }

    private bool Confirmed(string email) => f.WithDb(db => db.Users.AsNoTracking().Single(u => u.Email == email).EmailConfirmed);

    [Fact]
    public async Task Email_verification_confirms_with_valid_token()
    {
        var email = BlogFactory.NewEmail();
        await f.CreateUserAsync(email, verified: false);
        var (id, token) = await TokenFor(email);
        var c = f.NewClient();
        using var r = await c.GetAsync($"/email/verify/{id}/{token}");
        Assert.True(IsRedirect(r));
        Assert.True(Confirmed(email));
    }

    [Fact]
    public async Task Email_verification_rejects_tampered_token()
    {
        var email = BlogFactory.NewEmail();
        await f.CreateUserAsync(email, verified: false);
        var (id, token) = await TokenFor(email);
        var tampered = token[..^3] + (token.EndsWith("AAA") ? "BBB" : "AAA");
        var c = f.NewClient();
        using var r = await c.GetAsync($"/email/verify/{id}/{tampered}");
        Assert.True(IsRedirect(r));
        Assert.False(Confirmed(email));
        using var garbage = await c.GetAsync($"/email/verify/{id}/!!!not-base64!!!");
        Assert.False(Confirmed(email));
    }
}
