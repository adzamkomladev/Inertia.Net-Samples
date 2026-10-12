using System.ComponentModel.DataAnnotations;

namespace ReactSqliteReplicasBlog.Features.Auth;

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
