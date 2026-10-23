using System.Buffers.Text;
using System.Net;
using System.Text;
using Inertia.Net;
using Inertia.Net.Testing;
using Inkwell.Data;
using Inkwell.Features.Auth;
using Inkwell.Features.Team;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Inkwell.Tests;

/// The shared auth prop is cached per user and invalidated by profile, theme, verification and role changes.
public sealed class AuthCacheTests(BlogFactory app) : IClassFixture<BlogFactory>
{
    [Fact]
    public async Task Profile_and_theme_changes_refresh_the_shared_auth_prop()
    {
        var email = BlogFactory.NewEmail("auth-cache");
        await app.CreateUserAsync(email);
        var client = app.NewClient();
        await client.LoginOkAsync(email);
        var initialName = email.Split('@')[0];

        using (var initial = await client.GetAsync("/posts"))
            await initial.AssertInertiaAsync(p => p.Where("auth.user.name", initialName).Where("auth.user.theme", "system"));

        using (var profile = await client.SendAsync(HttpMethod.Put, "/settings/profile", new { displayName = "Cached Name", removeAvatar = false }))
            Assert.True(profile.StatusCode is HttpStatusCode.Redirect or HttpStatusCode.SeeOther or HttpStatusCode.OK);

        using (var afterProfile = await client.GetAsync("/posts"))
            await afterProfile.AssertInertiaAsync(p => p.Where("auth.user.name", "Cached Name"));

        using (var preferences = await client.SendAsync(HttpMethod.Put, "/settings/preferences", new
               {
                   theme = "dark",
                   emailOnComment = true,
                   emailOnLike = true,
                   weeklyDigest = false,
                   dailyRecommendationDigest = false,
                   timeZoneId = "UTC",
               }))
            Assert.True(preferences.StatusCode is HttpStatusCode.Redirect or HttpStatusCode.SeeOther or HttpStatusCode.OK);

        using (var afterTheme = await client.GetAsync("/posts"))
            await afterTheme.AssertInertiaAsync(p => p.Where("auth.user.theme", "dark"));
    }

    [Fact]
    public async Task Email_verification_refreshes_the_shared_auth_prop()
    {
        var email = BlogFactory.NewEmail("auth-verify");
        await app.CreateUserAsync(email, verified: false);
        var client = app.NewClient();
        await client.LoginOkAsync(email);

        using (var before = await client.GetAsync("/posts"))
            await before.AssertInertiaAsync(p => p.Where("auth.user.verified", false));

        string token;
        Guid id;
        using (var scope = app.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
            var user = (await users.FindByEmailAsync(email))!;
            id = user.Id;
            token = Base64Url.EncodeToString(Encoding.UTF8.GetBytes(await users.GenerateEmailConfirmationTokenAsync(user)));
        }

        using (var confirmed = await client.GetAsync($"/email/verify/{id}/{token}"))
            Assert.True(confirmed.StatusCode is HttpStatusCode.Redirect or HttpStatusCode.SeeOther or HttpStatusCode.OK);

        using (var after = await client.GetAsync("/posts"))
            await after.AssertInertiaAsync(p => p.Where("auth.user.verified", true));
    }

    [Fact]
    public async Task Role_changes_refresh_the_shared_auth_prop()
    {
        var email = BlogFactory.NewEmail("auth-role");
        var id = await app.CreateUserAsync(email);
        app.WithDb(db =>
        {
            var user = db.Users.Single(u => u.Id == id);
            user.IsAdmin = true;
            db.SaveChanges();
            return true;
        });

        var client = app.NewClient();
        await client.LoginOkAsync(email);
        using (var before = await client.GetAsync("/posts"))
            await before.AssertInertiaAsync(p => p.Where("auth.user.isAdmin", true));

        using (var scope = app.Services.CreateScope())
        {
            var outcome = await scope.ServiceProvider.GetRequiredService<TeamService>()
                .RemoveAsync(id, Guid.CreateVersion7(), CancellationToken.None);
            Assert.Equal("success", outcome.Flash?.Key);
        }

        using (var after = await client.GetAsync("/posts"))
            await after.AssertInertiaAsync(p => p.Where("auth.user.isAdmin", false));
    }

    [Fact]
    public async Task Shared_auth_serves_the_cached_value_until_invalidated()
    {
        var email = BlogFactory.NewEmail("auth-cache-hit");
        var id = await app.CreateUserAsync(email);
        var client = app.NewClient();
        await client.LoginOkAsync(email);
        var initialName = email.Split('@')[0];

        using (var first = await client.GetAsync("/posts"))
            await first.AssertInertiaAsync(p => p.Where("auth.user.name", initialName));

        // Bypass the service so the cache is not invalidated.
        app.WithDb(db =>
        {
            var user = db.Users.Single(u => u.Id == id);
            user.DisplayName = "Direct Change";
            db.SaveChanges();
            return true;
        });

        using (var cached = await client.GetAsync("/posts"))
            await cached.AssertInertiaAsync(p => p.Where("auth.user.name", initialName));

        using (var scope = app.Services.CreateAsyncScope())
            await AuthCache.InvalidateAsync(scope.ServiceProvider.GetRequiredService<IInertiaCache>(), id, default);

        using (var refreshed = await client.GetAsync("/posts"))
            await refreshed.AssertInertiaAsync(p => p.Where("auth.user.name", "Direct Change"));
    }
}
