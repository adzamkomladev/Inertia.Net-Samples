using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using MongoDB.Driver;
using SvelteMongoReplicasBlog.Data;
using SvelteMongoReplicasBlog.Infrastructure;
using static SvelteMongoReplicasBlog.Features.AuthHelpers;

namespace SvelteMongoReplicasBlog.Features;

public static partial class AuthService
{
    public static async Task SignInAsync(
        HttpContext ctx,
        User user,
        string? avatarKey,
        bool persistent
    ) =>
        await ctx.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            Principal(user, avatarKey),
            new AuthenticationProperties { IsPersistent = persistent }
        );

    /// Runs on every authenticated request: drops cookies for deleted users and keeps claims (verified, name, avatar, theme) fresh.
    public static async Task ValidatePrincipal(CookieValidatePrincipalContext context)
    {
        var id = context.Principal?.UserIdOrNull();
        var services = context.HttpContext.RequestServices;
        var db = services.GetRequiredService<AppDb>();
        var user = id is null
            ? null
            : await db
                .Users.Find(u => u.Id == id)
                .FirstOrDefaultAsync(context.HttpContext.RequestAborted);
        if (user is null)
        {
            context.RejectPrincipal();
            await context.HttpContext.SignOutAsync(
                CookieAuthenticationDefaults.AuthenticationScheme
            );
            return;
        }

        var fresh = Principal(
            user,
            await AvatarKeyAsync(db, user, context.HttpContext.RequestAborted)
        );
        var current = context.Principal!;
        if (
            Claim(current, VerifiedClaim) != Claim(fresh, VerifiedClaim)
            || Claim(current, ClaimTypes.Name) != Claim(fresh, ClaimTypes.Name)
            || Claim(current, ClaimTypes.Email) != Claim(fresh, ClaimTypes.Email)
            || Claim(current, AvatarClaim) != Claim(fresh, AvatarClaim)
            || Claim(current, ThemeClaim) != Claim(fresh, ThemeClaim)
        )
        {
            context.ReplacePrincipal(fresh);
            context.ShouldRenew = true;
        }
    }

    internal // The claim holds the asset key (stable); SharedAuth turns it into a URL (presigned URLs change every request).
    static async Task<string?> AvatarKeyAsync(AppDb db, User user, CancellationToken ct) =>
        user.AvatarAssetId is not { } assetId
            ? null
            : await db
                .Assets.Find(a => a.Id == assetId)
                .Project(a => a.Key)
                .FirstOrDefaultAsync(ct);
}
