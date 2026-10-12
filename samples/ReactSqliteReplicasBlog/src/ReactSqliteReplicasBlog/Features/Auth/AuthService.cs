using System.Buffers.Text;
using System.Security.Claims;
using System.Text;
using ReactSqliteReplicasBlog.Data;
using ReactSqliteReplicasBlog.Infrastructure;
using ReactSqliteReplicasBlog.Jobs;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using static ReactSqliteReplicasBlog.Features.Auth.AuthHelpers;

namespace ReactSqliteReplicasBlog.Features.Auth;

public static partial class AuthService
{
    internal static async Task<IResult> LoginAsync(
        [FromBody] LoginInput input,
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
            return Back()
                .WithErrors(
                    new Dictionary<string, string>
                    {
                        ["email"] = "Too many failed attempts. Please try again later.",
                    }
                );
        if (!result.Succeeded)
            return Back()
                .WithErrors(
                    new Dictionary<string, string>
                    {
                        ["email"] = "These credentials do not match our records.",
                    }
                );
        return Results.Redirect(IsLocal(returnUrl) ? returnUrl! : "/posts");
    }

    internal static async Task<IResult> RegisterAsync(
        [FromBody] RegisterInput input,
        HttpContext ctx,
        UserManager<AppUser> users,
        SignInManager<AppUser> signIn,
        AppDb db,
        JobQueue queue
    )
    {
        if (await users.FindByEmailAsync(input.Email) is not null)
            return Back()
                .WithErrors(
                    new Dictionary<string, string>
                    {
                        ["email"] = "This e-mail is already registered.",
                    }
                );
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
            return Back().WithErrors(errors);
        }

        queue.Enqueue(db, new SendVerificationEmail(user.Id));
        await db.SaveChangesAsync();
        queue.Wake();
        await signIn.SignInAsync(user, isPersistent: true);
        ctx.Inertia().Flash("success", "Account created! Check your inbox to verify your e-mail.");
        return Results.Redirect("/email/verify");
    }

    internal static async Task<IResult> LogoutAsync(HttpContext ctx, SignInManager<AppUser> signIn)
    {
        await signIn.SignOutAsync();
        ctx.Inertia().ClearHistory();
        return Results.Redirect("/login");
    }

    internal static async Task<IResult> ShowVerificationAsync(
        HttpContext ctx,
        UserManager<AppUser> users,
        SignInManager<AppUser> signIn
    )
    {
        var user = await users.GetUserAsync(ctx.User);
        if (user is null)
            return Results.Redirect("/login");
        if (!user.EmailConfirmed)
            return Render("Auth/VerifyEmail", new VerifyEmailProps(user.Email!, false));
        // Verified on another device: this cookie still says email_verified=false, so re-issue it.
        if (!ctx.User.IsVerified())
            await signIn.RefreshSignInAsync(user);
        return Results.Redirect("/posts");
    }

    internal static async Task<IResult> ConfirmEmailAsync(
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
        {
            ctx.Inertia().Flash("error", "This verification link is invalid or has expired.");
            return Results.Redirect(fallback);
        }

        // The cookie still carries email_verified=false: re-issue it for the signed-in owner.
        if (ctx.User.UserId() == user.Id)
            await signIn.RefreshSignInAsync(user);
        ctx.Inertia().Flash("success", "E-mail verified. Welcome!");
        return Results.Redirect("/posts");
    }

    internal static async Task<IResult> SendVerificationAsync(
        HttpContext ctx,
        UserManager<AppUser> users,
        AppDb db,
        JobQueue queue
    )
    {
        var user = await users.GetUserAsync(ctx.User);
        if (user is null)
            return Results.Redirect("/login");
        if (user.EmailConfirmed)
            return Back("/posts").WithFlash("info", "Your e-mail is already verified.");
        queue.Enqueue(db, new SendVerificationEmail(user.Id));
        await db.SaveChangesAsync();
        queue.Wake();
        return Back().WithFlash("success", "Verification link sent.");
    }
}

/// <summary>Adds the display name and the <c>email_verified</c> claim used by the "verified" policy.</summary>
public sealed class AppClaimsPrincipalFactory(
    UserManager<AppUser> users,
    IOptions<IdentityOptions> options
) : UserClaimsPrincipalFactory<AppUser>(users, options)
{
    protected override async Task<ClaimsIdentity> GenerateClaimsAsync(AppUser user)
    {
        var id = await base.GenerateClaimsAsync(user);
        id.AddClaim(
            new Claim(AuthPolicies.EmailVerifiedClaim, user.EmailConfirmed ? "true" : "false")
        );
        id.AddClaim(new Claim("display_name", user.DisplayName));
        return id;
    }
}
