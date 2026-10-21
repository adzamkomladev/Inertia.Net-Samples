using MongoDB.Driver;
using SvelteMongoReplicasBlog.Data;
using SvelteMongoReplicasBlog.Infrastructure;

namespace SvelteMongoReplicasBlog.Features;

public static class Preferences
{
    public static void MapPreferences(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/settings").RequireAuthorization();
        group.MapGet("/", () => Results.Redirect("/settings/preferences"));
        group.MapGet("/preferences", ShowPreferencesAsync);
        group
            .MapPut("/preferences", UpdatePreferencesAsync)
            .WithInertiaValidation();
        group.MapPut("/profile", UpdateProfileAsync).WithInertiaValidation();
    }

    static async Task<IResult> ShowPreferencesAsync(HttpContext ctx, AppDb db, CancellationToken ct)
    {
        var user = await PreferencesService.GetPreferencesAsync(ctx, db, ct);
        return Render(
            "Settings/Preferences",
            new InertiaProps
            {
                ["preferences"] = user.Preferences,
                ["profile"] = new { name = user.Name, avatarAssetId = user.AvatarAssetId },
            }
        );
    }

    static async Task<IResult> UpdatePreferencesAsync(
        PreferencesInput input,
        HttpContext ctx,
        AppDb db,
        CancellationToken ct
    ) => (await PreferencesService.UpdatePreferencesAsync(input, ctx, db, ct)).Respond(ctx);

    static async Task<IResult> UpdateProfileAsync(
        ProfileInput input,
        HttpContext ctx,
        AppDb db,
        CancellationToken ct
    ) => (await PreferencesService.UpdateProfileAsync(input, ctx, db, ct)).Respond(ctx);
}
