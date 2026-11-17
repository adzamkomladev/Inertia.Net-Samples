using Microsoft.AspNetCore.Authentication.Cookies;
using TechLandier.Features;
using TechLandier.Infrastructure;

namespace TechLandier.Realtime;

public static class AuthSetup
{
    public static WebApplicationBuilder AddRealtimeAuth(this WebApplicationBuilder builder)
    {
        var config = builder.Configuration;
        var secureCookies = config.GetValue("Auth:SecureCookies", !builder.Environment.IsDevelopment());
        builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
            .AddCookie(o =>
            {
                // Same cookie and key ring as MainApp: a session created by a web replica opens a hub here.
                o.Cookie.Name = ".tld.auth";
                o.Cookie.HttpOnly = true;
                o.Cookie.SameSite = SameSiteMode.Lax;
                o.Cookie.SecurePolicy = secureCookies ? CookieSecurePolicy.Always : CookieSecurePolicy.SameAsRequest;
                o.ExpireTimeSpan = TimeSpan.FromDays(30);
                o.SlidingExpiration = true;
                o.Events.OnValidatePrincipal = AuthHttp.ValidatePrincipal;
            });
        builder.Services.AddAuthorization();
        // AuthService reads sessions only; this role never stages jobs, so it does not run Runwick.
        builder.Services.AddSingleton<IJobOutbox, ReadOnlyJobOutbox>();
        builder.Services.AddScoped<AuthService>();
        return builder;
    }
}

/// <summary>Satisfies AuthService's constructor in the realtime role, which has no Runwick store and must not stage work.</summary>
internal sealed class ReadOnlyJobOutbox : IJobOutbox
{
    static InvalidOperationException Refused() => new("The realtime role does not stage jobs.");

    public async Task StageAsync<T>(TechLandier.Data.AppDb db, T job, CancellationToken ct = default) where T : class, Runwick.IQueueJob { await Task.CompletedTask; throw Refused(); }
    public async Task StageManyAsync(TechLandier.Data.AppDb db, IReadOnlyList<Runwick.IQueueJob> jobs, CancellationToken ct = default) { await Task.CompletedTask; throw Refused(); }
    public async Task DispatchAsync<T>(T job, CancellationToken ct = default) where T : class, Runwick.IQueueJob { await Task.CompletedTask; throw Refused(); }
    public async Task DispatchManyAsync(IReadOnlyList<Runwick.IQueueJob> jobs, CancellationToken ct = default) { await Task.CompletedTask; throw Refused(); }
}
