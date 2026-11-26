using System.Collections.Concurrent;
using System.Security.Claims;
using KitchenSink.Data;
using KitchenSink.Features.Crm;
using KitchenSink.Infrastructure;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;

namespace KitchenSink.Features.Auth;

public static class AuthFeature
{
    static readonly ConcurrentDictionary<string, Attempts> LoginAttempts = new(StringComparer.Ordinal);

    public static IEndpointRouteBuilder MapAuth(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/", () => Results.Redirect("/login")).AllowAnonymous();

        endpoints.MapGet("/login", (HttpContext context) =>
        {
            if (context.User.Identity?.IsAuthenticated == true)
            {
                return Results.Redirect("/dashboard");
            }

            return Inertia.Net.Inertia.Render("Auth/Login", new { status = (string?)null });
        }).AllowAnonymous();

        endpoints.MapPost("/login", async (HttpContext context, AppDb db, CancellationToken cancellationToken) =>
        {
            var input = await RequestInput.ReadAsync(context.Request, cancellationToken);
            var email = input.String("email")?.Trim().ToLowerInvariant();
            var password = input.String("password") ?? "";
            var errors = new Dictionary<string, string>(StringComparer.Ordinal);

            if (string.IsNullOrWhiteSpace(email))
            {
                errors["email"] = "The email field is required.";
            }
            else if (!InertiaForms.LooksLikeEmail(email))
            {
                errors["email"] = "The email field must be a valid email address.";
            }

            if (string.IsNullOrWhiteSpace(password))
            {
                errors["password"] = "The password field is required.";
            }

            if (errors.Count > 0)
            {
                return BackWithErrors(errors);
            }

            var key = $"{email}|{context.Connection.RemoteIpAddress?.ToString() ?? "unknown"}";
            var attempts = LoginAttempts.GetOrAdd(key, _ => new Attempts());
            if (attempts.TooMany())
            {
                return Results.StatusCode(StatusCodes.Status429TooManyRequests);
            }

            var user = await db.Users.FirstOrDefaultAsync(u => u.Email == email, cancellationToken);
            if (user is null || !DemoSeeder.Verify(user.PasswordHash, user, password))
            {
                attempts.Fail();
                return BackWithErrors(new Dictionary<string, string>
                {
                    ["email"] = "These credentials do not match our records.",
                });
            }

            LoginAttempts.TryRemove(key, out _);

            var claims = new List<Claim>
            {
                new(ClaimTypes.NameIdentifier, user.Id.ToString()),
                new(ClaimTypes.Name, user.Name),
                new(ClaimTypes.Email, user.Email),
            };
            var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
            var properties = new AuthenticationProperties
            {
                IsPersistent = input.Bool("remember"),
            };
            if (properties.IsPersistent)
            {
                properties.ExpiresUtc = DateTimeOffset.UtcNow.AddDays(30);
            }

            await context.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(identity), properties);
            return Results.Redirect("/dashboard");
        }).AllowAnonymous();

        endpoints.MapPost("/logout", async (HttpContext context) =>
        {
            await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return Results.Redirect("/");
        }).RequireAuthorization();

        return endpoints;
    }

    static IResult BackWithErrors(Dictionary<string, string> errors) =>
        Inertia.Net.Inertia.Back().WithErrors(errors);

    sealed class Attempts
    {
        DateTimeOffset _start = DateTimeOffset.UtcNow;
        int _count;

        public bool TooMany()
        {
            lock (this)
            {
                if (DateTimeOffset.UtcNow - _start >= TimeSpan.FromMinutes(1))
                {
                    _start = DateTimeOffset.UtcNow;
                    _count = 0;
                }

                return _count >= 5;
            }
        }

        public void Fail()
        {
            lock (this)
            {
                if (DateTimeOffset.UtcNow - _start >= TimeSpan.FromMinutes(1))
                {
                    _start = DateTimeOffset.UtcNow;
                    _count = 0;
                }

                _count++;
            }
        }
    }
}
