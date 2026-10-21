using System.Buffers.Text;
using System.ComponentModel.DataAnnotations;
using System.Text;
using Inkwell.Data;
using Inkwell.Infrastructure;
using Inkwell.Jobs;
using Microsoft.AspNetCore.Identity;

namespace Inkwell.Features.Auth;

public sealed class LoginInput
{
    [Required, EmailAddress]
    public string Email { get; set; } = "";

    [Required]
    public string Password { get; set; } = "";
    public bool Remember { get; set; }
}

public sealed class RegisterInput
{
    [Required, MaxLength(100)]
    public string Name { get; set; } = "";

    [Required, EmailAddress, MaxLength(256)]
    public string Email { get; set; } = "";

    [Required, MinLength(8), MaxLength(128)]
    public string Password { get; set; } = "";

    [Required, Compare(nameof(Password), ErrorMessage = "The passwords do not match.")]
    public string PasswordConfirmation { get; set; } = "";
}

public sealed record VerifyEmailProps(string Email, bool Verified);

public sealed class AuthService(
    UserManager<AppUser> users,
    SignInManager<AppUser> signIn,
    AppDb db,
    JobQueue jobs,
    TimeProvider time)
{
    public async Task<Outcome> LoginAsync(LoginInput input, string? returnUrl)
    {
        var user = await users.FindByEmailAsync(input.Email);
        var result = user is null
            ? Microsoft.AspNetCore.Identity.SignInResult.Failed
            : await signIn.PasswordSignInAsync(user.UserName!, input.Password, input.Remember, lockoutOnFailure: true);
        if (result.IsLockedOut) return Outcome.Fail("email", "Too many failed attempts. Please try again later.");
        if (!result.Succeeded) return Outcome.Fail("email", "These credentials do not match our records.");
        return Outcome.RedirectTo(Microsoft.AspNetCore.Http.HttpResults.RedirectHttpResult.IsLocalUrl(returnUrl) ? returnUrl! : "/posts");
    }

    public async Task<Outcome> RegisterAsync(RegisterInput input)
    {
        if (await users.FindByEmailAsync(input.Email) is not null)
            return Outcome.Fail("email", "This e-mail is already registered.");
        var user = new AppUser
        {
            UserName = input.Email,
            Email = input.Email,
            DisplayName = input.Name.Trim(),
            CreatedAt = time.GetUtcNow().UtcDateTime,
        };
        var created = await users.CreateAsync(user, input.Password);
        if (!created.Succeeded)
        {
            var errors = new Dictionary<string, string>();
            foreach (var e in created.Errors)
                errors.TryAdd(e.Code.Contains("Email", StringComparison.Ordinal) || e.Code.Contains("UserName", StringComparison.Ordinal) ? "email" : "password", e.Description);
            return Outcome.Fail(errors);
        }

        await QueueVerificationAsync(user);
        await signIn.SignInAsync(user, isPersistent: true);
        return Outcome.FlashThenRedirect("success", "Account created! Check your inbox to verify your e-mail.", "/email/verify");
    }

    public Task LogoutAsync() => signIn.SignOutAsync();

    /// <summary>The address to show on the verify page, or the URL to redirect to when there is nothing to verify.</summary>
    public async Task<(string? Redirect, string? Email)> ShowVerificationAsync(AppUser? user, bool verifiedClaim)
    {
        if (user is null) return ("/login", null);
        if (!user.EmailConfirmed) return (null, user.Email!);
        // Verified on another device: this cookie still says email_verified=false, so re-issue it.
        if (!verifiedClaim) await signIn.RefreshSignInAsync(user);
        return ("/posts", null);
    }

    public async Task<Outcome> ConfirmEmailAsync(Guid userId, string token, Guid? viewerId)
    {
        var user = await users.FindByIdAsync(userId.ToString());
        var confirmed = false;
        if (user is not null)
        {
            try
            {
                confirmed = user.EmailConfirmed
                    || (await users.ConfirmEmailAsync(user, Encoding.UTF8.GetString(Base64Url.DecodeFromChars(token)))).Succeeded;
            }
            catch (FormatException) { } // malformed token: treated as invalid
        }
        if (!confirmed)
            return Outcome.FlashThenRedirect("error", "This verification link is invalid or has expired.", viewerId is null ? "/login" : "/email/verify");

        // The cookie still carries email_verified=false: re-issue it for the signed-in owner.
        if (viewerId == user!.Id) await signIn.RefreshSignInAsync(user);
        return Outcome.FlashThenRedirect("success", "E-mail verified. Welcome!", "/posts");
    }

    public async Task<Outcome> SendVerificationAsync(AppUser? user)
    {
        if (user is null) return Outcome.RedirectTo("/login");
        if (user.EmailConfirmed) return Outcome.FlashBackTo("/posts", "info", "Your e-mail is already verified.");
        await QueueVerificationAsync(user);
        return Outcome.FlashBack("success", "Verification link sent.");
    }

    async Task QueueVerificationAsync(AppUser user)
    {
        jobs.Enqueue(db, new SendVerificationEmail(user.Id));
        await db.SaveChangesAsync();
        jobs.Wake();
    }
}
