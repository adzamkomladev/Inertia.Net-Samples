using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using MongoDB.Driver;
using CarParker.Data;
using CarParker.Infrastructure;

namespace CarParker.Features;

public sealed class LoginInput
{
    [Required, EmailAddress] public string? Email { get; set; }
    [Required] public string? Password { get; set; }
    public bool Remember { get; set; }
    public string? ReturnUrl { get; set; }
}

public sealed class RegisterInput
{
    [Required, StringLength(80, MinimumLength = 2)] public string? Name { get; set; }
    [Required, EmailAddress, StringLength(254)] public string? Email { get; set; }
    [Required, StringLength(128, MinimumLength = 8)] public string? Password { get; set; }
    [Required, Compare(nameof(Password), ErrorMessage = "The passwords do not match.")] public string? PasswordConfirmation { get; set; }
}

public sealed record AuthAccount(User User, string? AvatarKey);
public sealed record RegisterResult(Outcome Outcome, AuthAccount? Account);
