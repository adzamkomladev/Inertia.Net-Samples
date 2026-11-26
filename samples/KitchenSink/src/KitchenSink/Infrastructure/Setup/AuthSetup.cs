using Microsoft.AspNetCore.Authentication.Cookies;

namespace KitchenSink.Infrastructure.Setup;

public static class AuthSetup
{
    public static WebApplicationBuilder AddKitchenSinkAuth(this WebApplicationBuilder builder)
    {
        builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
            .AddCookie(o =>
            {
                o.LoginPath = "/login";
                o.LogoutPath = "/logout";
                o.AccessDeniedPath = "/login";
                o.Cookie.Name = ".ks.auth";
                o.Cookie.HttpOnly = true;
                o.Cookie.SameSite = SameSiteMode.Lax;
                // Forwarded headers make this HTTPS behind a TLS-terminating proxy. SameAsRequest keeps the
                // local `docker compose up` flow usable over plain http://localhost.
                o.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
                o.ExpireTimeSpan = TimeSpan.FromDays(30);
                o.SlidingExpiration = true;
                // Inertia visits follow the login redirect like browser navigations.
                o.UseInertiaRedirects();
            });

        builder.Services.AddAuthorization();
        builder.Services.AddDistributedMemoryCache();
        builder.Services.AddSession(o =>
        {
            o.Cookie.Name = ".ks.session";
            o.Cookie.HttpOnly = true;
            o.Cookie.IsEssential = true;
            o.Cookie.SameSite = SameSiteMode.Lax;
            o.IdleTimeout = TimeSpan.FromHours(2);
        });
        builder.Services.AddAntiforgery(o =>
        {
            o.HeaderName = "X-XSRF-TOKEN";
            o.Cookie.Name = ".ks.af";
        });

        return builder;
    }
}
