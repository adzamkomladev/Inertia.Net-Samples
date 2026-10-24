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
        builder.Services.AddJobQueue();
        builder.Services.AddScoped<AuthService>();
        return builder;
    }
}
