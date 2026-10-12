using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using VuePgReplicasAdvBlog.Data;
using VuePgReplicasAdvBlog.Features.Auth;
using VuePgReplicasAdvBlog.Infrastructure;

namespace VuePgReplicasAdvBlog.Realtime;

public static class AuthSetup
{
    public static WebApplicationBuilder AddRealtimeAuth(this WebApplicationBuilder builder)
    {
        var secureCookies = builder.Configuration.GetValue("Auth:SecureCookies", !builder.Environment.IsDevelopment());
        builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
            .AddCookie(o =>
            {
                o.Cookie.Name = ".vpgmra.auth";
                o.Cookie.HttpOnly = true;
                o.Cookie.SameSite = SameSiteMode.Lax;
                o.Cookie.SecurePolicy = secureCookies ? CookieSecurePolicy.Always : CookieSecurePolicy.SameAsRequest;
                o.ExpireTimeSpan = TimeSpan.FromDays(30);
                o.SlidingExpiration = true;
                o.Events.OnValidatePrincipal = async context =>
                {
                    var principal = await AuthService.GetPrincipalAsync(context.Principal?.UserIdOrNull(),
                        CookieAuthenticationDefaults.AuthenticationScheme,
                        context.HttpContext.RequestServices.GetRequiredService<AppDb>(), context.HttpContext.RequestAborted);
                    if (principal is null)
                    {
                        context.RejectPrincipal();
                        await context.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
                    }
                    else if (AuthHelpers.NeedsRefresh(context.Principal!, principal))
                    {
                        context.ReplacePrincipal(principal);
                        context.ShouldRenew = true;
                    }
                };
            });
        builder.Services.AddAuthorization();
        return builder;
    }
}
