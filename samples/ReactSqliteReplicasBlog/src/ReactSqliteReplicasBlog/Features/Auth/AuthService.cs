using System.Buffers.Text;
using System.Security.Claims;
using System.Text;
using ReactSqliteReplicasBlog.Data;
using ReactSqliteReplicasBlog.Infrastructure;
using ReactSqliteReplicasBlog.Jobs;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using static ReactSqliteReplicasBlog.Features.Auth.AuthHelpers;

namespace ReactSqliteReplicasBlog.Features.Auth;

public static partial class AuthService
{
    internal static async Task<Outcome> LoginAsync(
        LoginInput input,
        string? returnUrl,
        UserManager<AppUser> users,
        SignInManager<AppUser> signIn
    )
    {
        var user = await users.FindByEmailAsync(input.Email);
        var result = user is null
            ? Microsoft.AspNetCore.Identity.SignInResult.Failed
            : await signIn.PasswordSignInAsync(
                user.UserName!,
                input.Password,
                input.Remember,
                lockoutOnFailure: true
            );
        if (result.IsLockedOut)
            return Outcome.Fail("email", "Too many failed attempts. Please try again later.");
        if (!result.Succeeded)
            return Outcome.Fail("email", "These credentials do not match our records.");
        return Outcome.RedirectTo(IsLocal(returnUrl) ? returnUrl! : "/posts");
    }

    internal static async Task<Outcome> RegisterAsync(
        RegisterInput input,
        HttpContext ctx,
        UserManager<AppUser> users,
        SignInManager<AppUser> signIn,
        AppDb db,
        JobQueue queue
    )
    {
        if (await users.FindByEmailAsync(input.Email) is not null)
            return Outcome.Fail("email", "This e-mail is already registered.");
        var user = new AppUser
        {
            UserName = input.Email,
            Email = input.Email,
            DisplayName = input.Name.Trim(),
            CreatedAt = DateTime.UtcNow,
        };
        var created = await users.CreateAsync(user, input.Password);
        if (!created.Succeeded)
        {
            var errors = new Dictionary<string, string>();
            foreach (var e in created.Errors)
                errors.TryAdd(
                    e.Code.Contains("Email", StringComparison.Ordinal)
                    || e.Code.Contains("UserName", StringComparison.Ordinal)
                        ? "email"
                        : "password",
                    e.Description
                );
            return Outcome.Fail(errors);
        }

        queue.Enqueue(db, new SendVerificationEmail(user.Id));
        await db.SaveChangesAsync();
        queue.Wake();
        await signIn.SignInAsync(user, isPersistent: true);
        return Outcome.FlashThenRedirect(
            "success",
            "Account created! Check your inbox to verify your e-mail.",
            "/email/verify"
        );
    }

    internal static async Task LogoutAsync(HttpContext ctx, SignInManager<AppUser> signIn) =>
        await signIn.SignOutAsync();

    /// <summary>Redirect when the session is anonymous or already verified; Email set means render the verify page.</summary>
    internal sealed record VerificationView(string? Redirect, string? Email);

    internal static async Task<VerificationView> ShowVerificationAsync(
        HttpContext ctx,
        UserManager<AppUser> users,
        SignInManager<AppUser> signIn
    )
    {
        var user = await users.GetUserAsync(ctx.User);
        if (user is null)
            return new VerificationView("/login", null);
        if (!user.EmailConfirmed)
            return new VerificationView(null, user.Email!);
        // Verified on another device: this cookie still says email_verified=false, so re-issue it.
        if (!ctx.User.IsVerified())
            await signIn.RefreshSignInAsync(user);
        return new VerificationView("/posts", null);
    }

    internal static async Task<Outcome> ConfirmEmailAsync(
        Guid userId,
        string token,
        HttpContext ctx,
        UserManager<AppUser> users,
        SignInManager<AppUser> signIn
    )
    {
        var fallback = ctx.User.Identity?.IsAuthenticated == true ? "/email/verify" : "/login";
        var user = await users.FindByIdAsync(userId.ToString());
        IdentityResult? result = null;
        if (user is not null)
        {
            try
            {
                result = user.EmailConfirmed
                    ? IdentityResult.Success
                    : await users.ConfirmEmailAsync(
                        user,
                        Encoding.UTF8.GetString(Base64Url.DecodeFromChars(token))
                    );
            }
            catch (FormatException) { } // malformed token: treated as invalid
        }

        if (user is null || result is not { Succeeded: true })
            return Outcome.FlashThenRedirect(
                "error",
                "This verification link is invalid or has expired.",
                fallback
            );

        // The cookie still carries email_verified=false: re-issue it for the signed-in owner.
        if (ctx.User.UserId() == user.Id)
            await signIn.RefreshSignInAsync(user);
        return Outcome.FlashThenRedirect("success", "E-mail verified. Welcome!", "/posts");
    }

    internal static async Task<Outcome> SendVerificationAsync(
        HttpContext ctx,
        UserManager<AppUser> users,
        AppDb db,
        JobQueue queue
    )
    {
        var user = await users.GetUserAsync(ctx.User);
        if (user is null)
            return Outcome.RedirectTo("/login");
        if (user.EmailConfirmed)
            return Outcome.FlashBackTo("/posts", "info", "Your e-mail is already verified.");
        queue.Enqueue(db, new SendVerificationEmail(user.Id));
        await db.SaveChangesAsync();
        queue.Wake();
        return Outcome.FlashBack("success", "Verification link sent.");
    }
}
