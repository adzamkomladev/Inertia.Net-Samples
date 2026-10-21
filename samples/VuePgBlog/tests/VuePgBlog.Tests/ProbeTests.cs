using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Logging;
using Microsoft.EntityFrameworkCore;
using VuePgBlog.Data;

namespace VuePgBlog.Tests;

public sealed class QuietFactory : AppFactory
{
    protected override void ConfigureWebHost(Microsoft.AspNetCore.Hosting.IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureLogging(logging =>
        {
            logging.ClearProviders();
            logging.AddConsole(); // console goes to the test host's stdout (visible with verbosity=detailed)
        });
    }
}

public sealed class BindingProbeTests(QuietFactory app) : IClassFixture<QuietFactory>
{
    [Fact]
    public async Task Probe_simple_type_binding()
    {
        var (client, userId) = await app.SignUpAsync("probe@example.test");
        var seeded = await app.SeedProductAsync("Probe Phone", price: 100m, stock: 5);

        // JSON body to a simple-type endpoint (POST /account/support takes string? subject, ...).
        using var jsonPost = await app.SendAsync(client, "/account/support", new { subject = "Probe subject", category = "other", message = "probe message" });
        var jsonBody = await jsonPost.Content.ReadAsStringAsync();
        var viaJson = await app.QueryAsync(db => db.Tickets.AsNoTracking().CountAsync(t => t.Subject == "Probe subject"));

        // Query string to the same endpoint.
        using var queryPost = await app.SendAsync(client, "/account/support?subject=Query%20subject&category=other&message=query", new { });
        var queryBody = await queryPost.Content.ReadAsStringAsync();
        var viaQuery = await app.QueryAsync(db => db.Tickets.AsNoTracking().CountAsync(t => t.Subject == "Query subject"));

        throw new Exception($"""
            jsonPost: {(int)jsonPost.StatusCode} loc={jsonPost.Headers.Location} viaJson={viaJson} body={Trunc(jsonBody)}
            queryPost: {(int)queryPost.StatusCode} loc={queryPost.Headers.Location} viaQuery={viaQuery} body={Trunc(queryBody)}
            """);

        static string Trunc(string s) => s.Length > 400 ? s[..400] : s;
    }
}
