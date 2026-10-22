using System.Security.Claims;
using Inkwell.Data;
using Inkwell.Infrastructure;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace Inkwell.Features.Auth;

/// <summary>Adds the display name and the <c>email_verified</c> claim used by the "verified" policy.</summary>
public sealed class AppClaimsPrincipalFactory(UserManager<AppUser> users, IOptions<IdentityOptions> options)
    : UserClaimsPrincipalFactory<AppUser>(users, options)
{
    protected override async Task<ClaimsIdentity> GenerateClaimsAsync(AppUser user)
    {
        var id = await base.GenerateClaimsAsync(user);
        id.AddClaim(new Claim(AuthPolicies.EmailVerifiedClaim, user.EmailConfirmed ? "true" : "false"));
        id.AddClaim(new Claim("display_name", user.DisplayName));
        return id;
    }
}
