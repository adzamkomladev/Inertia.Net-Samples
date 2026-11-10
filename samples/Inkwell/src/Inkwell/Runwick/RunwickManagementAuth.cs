using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Inkwell.Infrastructure.Setup;

/// <summary>The sample's single dashboard operator. Override with Runwick:Management:Username/Password.</summary>
public sealed record RunwickAdminCredentials(string Username, string Password)
{
    public bool Matches(string username, string password)
    {
        var userMatches = CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(Username), Encoding.UTF8.GetBytes(username));
        var passwordMatches = CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(Password), Encoding.UTF8.GetBytes(password));
        return userMatches & passwordMatches;
    }
}

/// <summary>
/// A cookie sign-in for the Runwick dashboard and its management API. The app's customer/admin cookie
/// scheme is intentionally separate: this login only grants Runwick management permissions.
/// </summary>
public static class RunwickManagementAuth
{
    public const string Scheme = "RunwickCookies";
    public const string LoginPath = "/runwick-login";
    public const string LogoutPath = "/runwick-logout";
    public const string DefaultUsername = "Admin";
    public const string DefaultPassword = "Admin@12345";

    public static RunwickAdminCredentials Credentials(IConfiguration config) => new(
        config["Runwick:Management:Username"] is { Length: > 0 } username ? username : DefaultUsername,
        config["Runwick:Management:Password"] is { Length: > 0 } password ? password : DefaultPassword);

    public static WebApplicationBuilder AddRunwickManagementAuth(this WebApplicationBuilder builder, RunwickAdminCredentials credentials)
    {
        builder.Services.AddSingleton(credentials);
        builder.Services.AddAuthentication().AddCookie(Scheme, o =>
        {
            o.LoginPath = LoginPath;
            o.LogoutPath = LogoutPath;
            o.AccessDeniedPath = LoginPath;
            o.Cookie.Name = ".runwick.auth";
            o.Cookie.HttpOnly = true;
            o.Cookie.SameSite = SameSiteMode.Lax;
            o.Cookie.SecurePolicy = builder.Environment.IsDevelopment() ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
            o.ExpireTimeSpan = TimeSpan.FromDays(7);
            o.SlidingExpiration = true;
            // The dashboard is mounted as a path branch, so its PathBase is /runwick. The default redirect
            // would point at /runwick/runwick-login; keep the login page outside the mount instead.
            o.Events.OnRedirectToLogin = async context =>
            {
                context.Response.Redirect($"{LoginPath}?ReturnUrl={Uri.EscapeDataString(CurrentPath(context.Request))}");
                await Task.CompletedTask;
            };
            o.Events.OnRedirectToAccessDenied = async context =>
            {
                context.Response.Redirect($"{LoginPath}?denied=1&ReturnUrl={Uri.EscapeDataString(CurrentPath(context.Request))}");
                await Task.CompletedTask;
            };
        });
        return builder;
    }

    private static string CurrentPath(HttpRequest request) =>
        request.PathBase + request.Path + request.QueryString;

    public static IEndpointRouteBuilder MapRunwickManagementLogin(this IEndpointRouteBuilder app)
    {
        app.MapGet(LoginPath, (HttpContext context, IAntiforgery antiforgery, string? returnUrl) =>
            RenderLogin(context, antiforgery, returnUrl, error: null, StatusCodes.Status200OK));

        app.MapPost(LoginPath, async (HttpContext context, IAntiforgery antiforgery, RunwickAdminCredentials credentials) =>
        {
            var form = await context.Request.ReadFormAsync(context.RequestAborted);
            var username = form["username"].ToString();
            var password = form["password"].ToString();
            var returnUrl = form["returnUrl"].ToString();
            if (!credentials.Matches(username, password))
            {
                return RenderLogin(context, antiforgery, returnUrl, "Invalid username or password.", StatusCodes.Status401Unauthorized);
            }

            var identity = new ClaimsIdentity(
            [
                new Claim(ClaimTypes.NameIdentifier, credentials.Username),
                new Claim(ClaimTypes.Name, credentials.Username),
                new Claim(ClaimTypes.Role, "runwick-admin"),
            ], Scheme);
            await context.SignInAsync(Scheme, new ClaimsPrincipal(identity),
                new AuthenticationProperties { IsPersistent = true, ExpiresUtc = DateTimeOffset.UtcNow.AddDays(7) });
            return Results.LocalRedirect(SafeReturnUrl(returnUrl));
        });

        // The dashboard's sign-out form posts here without a dashboard token; SameSite=Lax plus an
        // antiforgery-exempt POST is the sample's trade-off for a one-click sign-out.
        app.MapPost(LogoutPath, async (HttpContext context) =>
        {
            await context.SignOutAsync(Scheme);
            return Results.LocalRedirect("/");
        }).DisableAntiforgery();

        return app;
    }

    private static IResult RenderLogin(HttpContext context, IAntiforgery antiforgery, string? returnUrl, string? error, int status)
    {
        var tokens = antiforgery.GetAndStoreTokens(context);
        var html = HtmlEncoder.Default;
        var errorHtml = error is null ? "" : $"<p class=\"error\">{html.Encode(error)}</p>";
        var returnValue = html.Encode(SafeReturnUrl(returnUrl));
        var body = $$"""
            <!DOCTYPE html>
            <html lang="en">
            <head>
              <meta charset="utf-8">
              <meta name="viewport" content="width=device-width, initial-scale=1">
              <title>Runwick · sign in</title>
              <style>
                :root { color-scheme: light dark; font-family: ui-sans-serif, system-ui, sans-serif; }
                body { min-height: 100vh; margin: 0; display: grid; place-items: center; background: #0b1020; color: #e8ecf7; }
                main { width: min(22rem, calc(100vw - 2rem)); padding: 2rem; border: 1px solid #263252; border-radius: 1rem; background: #111a30; box-shadow: 0 24px 64px #0007; }
                h1 { margin: 0 0 .25rem; font-size: 1.5rem; }
                p.lede { margin: 0 0 1.5rem; color: #9eabc8; font-size: .9rem; }
                label { display: block; margin: 1rem 0 .35rem; font-size: .85rem; color: #b8c4dd; }
                input { width: 100%; box-sizing: border-box; padding: .7rem .8rem; border: 1px solid #33415f; border-radius: .55rem; background: #0b1428; color: inherit; font: inherit; }
                button { width: 100%; margin-top: 1.4rem; padding: .75rem; border: 0; border-radius: .55rem; background: #5b8cff; color: #071022; font: inherit; font-weight: 700; cursor: pointer; }
                .error { padding: .65rem .75rem; border-radius: .5rem; background: #3b1522; color: #ffb4c4; font-size: .85rem; }
                a { color: #8fb0ff; font-size: .85rem; }
              </style>
            </head>
            <body>
              <main>
                <h1>Runwick</h1>
                <p class="lede">TechLandier job operations</p>
                {{errorHtml}}
                <form method="post" action="{{LoginPath}}">
                  <input type="hidden" name="__RequestVerificationToken" value="{{html.Encode(tokens.RequestToken ?? "")}}">
                  <input type="hidden" name="returnUrl" value="{{returnValue}}">
                  <label for="username">Username</label>
                  <input id="username" name="username" autocomplete="username" required autofocus>
                  <label for="password">Password</label>
                  <input id="password" name="password" type="password" autocomplete="current-password" required>
                  <button type="submit">Sign in</button>
                </form>
                <p><a href="/">Back to the store</a></p>
              </main>
            </body>
            </html>
            """;
        return Results.Content(body, "text/html", Encoding.UTF8, status);
    }

    private static string SafeReturnUrl(string? returnUrl) =>
        returnUrl is { Length: > 1 } value && value[0] == '/' && !value.StartsWith("//", StringComparison.Ordinal) && !value.Contains('\\')
            ? value
            : "/runwick";
}
