using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Inkwell.Data;
using Inkwell.Features.Team;
namespace Inkwell.Tests;
public sealed class TeamTests(BlogFactory app) : IClassFixture<BlogFactory>
{
    [Fact]
    public async Task Admin_policy_uses_current_account_state_and_preserves_authors()
    {
        var email = BlogFactory.NewEmail("admin");
        var id = await app.CreateUserAsync(email);
        var client = app.NewClient();
        await client.LoginOkAsync(email);
        using var denied = await client.GetAsync("/admin/team");
        Assert.NotEqual(HttpStatusCode.OK, denied.StatusCode);
        app.WithDb(db => db.Users.Where(u => u.Id == id).ExecuteUpdate(s => s.SetProperty(u => u.IsAdmin, true)));
        using var allowed = await client.GetAsync("/admin/team");
        Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);
        await using var scope = app.Services.CreateAsyncScope();
        var svc = scope.ServiceProvider.GetRequiredService<TeamService>();
        Assert.NotNull((await svc.CreateAsync(new("Staff", "bad", "Password@12345"))).Errors);
        var staffEmail = BlogFactory.NewEmail("staff");
        Assert.Null((await svc.CreateAsync(new("Staff", staffEmail, "Password@12345"))).Errors);
        Assert.NotNull((await svc.CreateAsync(new("Staff", staffEmail, "Password@12345"))).Errors);
        Assert.NotNull((await svc.RemoveAsync(id, id, default)).Errors);
        var staffId = app.WithDb(db => db.Users.Single(u => u.Email == staffEmail).Id);
        Assert.Null((await svc.RemoveAsync(id, staffId, default)).Errors);
        using var revoked = await client.GetAsync("/admin/team");
        Assert.NotEqual(HttpStatusCode.OK, revoked.StatusCode);
        Assert.False(app.WithDb(db => db.Users.Single(u => u.Id == id).IsAdmin));
    }

    [Fact]
    public async Task Promoting_gives_an_existing_verified_writer_admin_access_once()
    {
        var email = BlogFactory.NewEmail("writer");
        var id = await app.CreateUserAsync(email);
        await using var scope = app.Services.CreateAsyncScope();
        var svc = scope.ServiceProvider.GetRequiredService<TeamService>();

        Assert.NotNull((await svc.PromoteAsync(new(BlogFactory.NewEmail("nobody")))).Errors);
        Assert.Null((await svc.PromoteAsync(new(email.ToUpperInvariant()))).Errors);
        Assert.True(app.WithDb(db => db.Users.Single(u => u.Id == id).IsAdmin));
        Assert.NotNull((await svc.PromoteAsync(new(email))).Errors);
    }
}
