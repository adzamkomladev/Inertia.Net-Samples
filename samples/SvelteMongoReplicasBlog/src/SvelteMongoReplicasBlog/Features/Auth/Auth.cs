using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using MongoDB.Driver;
using SvelteMongoReplicasBlog.Data;
using SvelteMongoReplicasBlog.Infrastructure;
using static SvelteMongoReplicasBlog.Features.AuthHelpers;

namespace SvelteMongoReplicasBlog.Features;

public static class Auth
{
    public static void MapAuth(this IEndpointRouteBuilder app)
    {
        app.MapGet(
            "/login",
            (HttpContext ctx, string? returnUrl) =>
                ctx.User.Identity?.IsAuthenticated == true
                    ? Results.Redirect("/posts")
                    : Render(
                        "Auth/Login",
                        new InertiaProps { ["returnUrl"] = SafeReturnUrl(returnUrl) }
                    )
        );
        app.MapPost("/login", LoginAsync)
            .WithInertiaValidation()
            .RequireRateLimiting("auth");
        app.MapGet(
            "/register",
            (HttpContext ctx) =>
                ctx.User.Identity?.IsAuthenticated == true
                    ? Results.Redirect("/posts")
                    : Render("Auth/Register")
        );
        app.MapPost("/register", RegisterAsync)
            .WithInertiaPrecognition()
            .RequireRateLimiting("auth");
        app.MapPost("/logout", (Delegate)LogoutAsync);
        app.MapGet(
                "/email/verify",
                (HttpContext ctx) =>
                    ctx.User.HasClaim(VerifiedClaim, "true")
                        ? Results.Redirect("/posts")
                        : Render("Auth/VerifyEmail")
            )
            .RequireAuthorization();
        app.MapPost("/email/verification-notification", SendVerificationAsync)
            .RequireAuthorization()
            .RequireRateLimiting("auth");
        // Works signed in or not (link may be opened in another browser). ValidatePrincipal refreshes the claim.
        app.MapGet("/email/verify/confirm", ConfirmEmailAsync);
    }

    internal static async Task<IResult> LoginAsync(
        LoginInput input,
        HttpContext ctx,
        AppDb db,
        CancellationToken ct
    )
    {
        var email = Normalize(input.Email!);
        var user = await db.Users.Find(u => u.Email == email).FirstOrDefaultAsync(ct);
        var verified = Hasher.VerifyHashedPassword(
            user ?? DummyUser,
            user?.PasswordHash ?? DummyHash,
            input.Password!
        );
        var result = user is null ? PasswordVerificationResult.Failed : verified;
        if (result == PasswordVerificationResult.Failed)
            return Back()
                .WithErrors(
                    new Dictionary<string, string>
                    {
                        ["email"] = "These credentials do not match our records.",
                    }
                );
        if (result == PasswordVerificationResult.SuccessRehashNeeded)
        {
            user!.PasswordHash = Hasher.HashPassword(user, input.Password!);
            await db.Users.UpdateOneAsync(
                u => u.Id == user.Id,
                Builders<User>.Update.Set(u => u.PasswordHash, user.PasswordHash),
                cancellationToken: ct
            );
        }

        await AuthService.SignInAsync(ctx, user!, await AuthService.AvatarKeyAsync(db, user!, ct), input.Remember);
        return Results.Redirect(SafeReturnUrl(input.ReturnUrl) ?? "/posts");
    }

    internal static async Task<IResult> RegisterAsync(
        RegisterInput input,
        HttpContext ctx,
        AppDb db,
        JobQueue jobs,
        IDataProtectionProvider dp,
        IConfiguration config,
        CancellationToken ct
    )
    {
        var email = Normalize(input.Email!);
        if (await db.Users.Find(u => u.Email == email).AnyAsync(ct))
            return Back()
                .WithErrors(
                    new Dictionary<string, string>
                    {
                        ["email"] = "This email is already registered.",
                    }
                );
        var user = new User { Email = email, Name = input.Name!.Trim() };
        user.PasswordHash = Hasher.HashPassword(user, input.Password!);
        try
        {
            await db.TransactionAsync(
                async session =>
                {
                    await db.Users.InsertOneAsync(session, user, cancellationToken: ct);
                    await jobs.EnqueueAsync(
                        db,
                        Emails.Verify(user.Email, user.Name, VerifyUrl(ctx, config, dp, user)),
                        session: session,
                        ct: ct
                    );
                },
                ct
            );
            jobs.Wake();
        }
        catch (MongoWriteException e)
            when (e.WriteError.Category == ServerErrorCategory.DuplicateKey)
        {
            return Back()
                .WithErrors(
                    new Dictionary<string, string>
                    {
                        ["email"] = "This email is already registered.",
                    }
                );
        }

        await AuthService.SignInAsync(ctx, user, null, persistent: false);
        return Results.Redirect("/email/verify");
    }

    internal static async Task<IResult> LogoutAsync(HttpContext ctx)
    {
        await ctx.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return Results.Redirect("/login");
    }

    internal static async Task<IResult> SendVerificationAsync(
        HttpContext ctx,
        AppDb db,
        JobQueue jobs,
        IDataProtectionProvider dp,
        IConfiguration config,
        CancellationToken ct
    )
    {
        var user = await db.Users.Find(u => u.Id == ctx.User.UserId()).FirstOrDefaultAsync(ct);
        if (user is null || user.EmailVerifiedAt is not null)
            return Results.Redirect("/posts");
        await jobs.EnqueueAsync(
            db,
            Emails.Verify(user.Email, user.Name, VerifyUrl(ctx, config, dp, user)),
            ct: ct
        );
        return Back()
            .WithFlash("toast", new Toast("success", "A new verification link has been sent."));
    }

    internal static async Task<IResult> ConfirmEmailAsync(
        string? token,
        HttpContext ctx,
        AppDb db,
        IDataProtectionProvider dp,
        CancellationToken ct
    )
    {
        string payload;
        try
        {
            payload = Protector(dp).Unprotect(token ?? "");
        }
        catch (System.Security.Cryptography.CryptographicException)
        {
            ctx.Inertia()
                .Flash(
                    "toast",
                    new Toast("error", "That verification link is invalid or has expired.")
                );
            return Results.Redirect(
                ctx.User.Identity?.IsAuthenticated == true ? "/email/verify" : "/login"
            );
        }

        var parts = payload.Split(':', 2);
        var user = await db.Users.Find(u => u.Id == Guid.Parse(parts[0])).FirstOrDefaultAsync(ct);
        if (
            user is null
            || !string.Equals(user.Email, parts[1], StringComparison.OrdinalIgnoreCase)
        )
        {
            ctx.Inertia()
                .Flash("toast", new Toast("error", "That verification link is no longer valid."));
            return Results.Redirect("/login");
        }

        if (user.EmailVerifiedAt is null)
            await db.Users.UpdateOneAsync(
                u => u.Id == user.Id && u.Email == user.Email && u.EmailVerifiedAt == null,
                Builders<User>.Update.Set(u => u.EmailVerifiedAt, DateTime.UtcNow),
                cancellationToken: ct
            );
        ctx.Inertia().Flash("toast", new Toast("success", "Email verified. Welcome aboard!"));
        return Results.Redirect(ctx.User.Identity?.IsAuthenticated == true ? "/posts" : "/login");
    }
}
