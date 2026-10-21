using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;
using TechLandier.Data;
using TechLandier.Infrastructure;
using static TechLandier.Features.UploadsHelpers;

namespace TechLandier.Features;

public static partial class UploadsService
{
    internal static async Task<IResult> PresignAsync(
        PresignInput input,
        HttpContext ctx,
        AppDb db,
        Storage storage,
        CancellationToken ct
    )
    {
        var results = new List<ValidationResult>();
        if (!Validator.TryValidateObject(input, new ValidationContext(input), results, true))
            return Results.ValidationProblem(
                results
                    .SelectMany(r =>
                        (r.MemberNames.Any() ? r.MemberNames : [""]).Select(m =>
                            (m, r.ErrorMessage ?? "Invalid.")
                        )
                    )
                    .GroupBy(x => x.m)
                    .ToDictionary(g => g.Key, g => g.Select(x => x.Item2).ToArray()),
                statusCode: StatusCodes.Status422UnprocessableEntity
            );
        var contentType = input.ContentType!.Trim().ToLowerInvariant();
        if (!Allowed.TryGetValue(contentType, out var rule))
            return Invalid("contentType", "Only PNG, JPEG, WebP, GIF images and PDFs are allowed.");
        if (input.Size > rule.Max)
            return Invalid("size", $"File is too large (max {rule.Max / Mb} MB).");
        var userId = ctx.User.UserId();
        var now = DateTime.UtcNow;
        var key = $"u/{userId}/{now:yyyy}/{now:MM}/{Guid.NewGuid():N}{rule.Ext}";
        var asset = new Asset
        {
            OwnerId = userId,
            Key = key,
            ContentType = contentType,
            Size = input.Size,
        };
        db.Assets.Add(asset);
        await db.SaveChangesAsync(ct);
        // ponytail: after confirm, the still-valid presigned PUT (up to 2 min) could overwrite the confirmed object; upgrade path: copy to a final key on confirm.
        return Results.Ok(
            new
            {
                assetId = asset.Id,
                url = storage.PresignPut(key, contentType, TimeSpan.FromMinutes(2)),
                headers = new Dictionary<string, string> { ["Content-Type"] = contentType },
            }
        );
    }

    internal static async Task<IResult> ConfirmAsync(
        Guid id,
        HttpContext ctx,
        AppDb db,
        Storage storage,
        CancellationToken ct
    )
    {
        var userId = ctx.User.UserId();
        var asset = await db.Assets.FirstOrDefaultAsync(a => a.Id == id && a.OwnerId == userId, ct);
        if (asset is null)
            return Results.NotFound();
        if (asset.Status != AssetStatus.Ready)
        {
            var head = await storage.HeadAsync(asset.Key, ct);
            if (head is null)
                return Invalid("file", "Upload not found");
            var max = Allowed.TryGetValue(asset.ContentType, out var rule) ? rule.Max : 0;
            var actualType = head.Value.ContentType?.Split(';')[0].Trim();
            if (
                head.Value.Size > max
                || !string.Equals(actualType, asset.ContentType, StringComparison.OrdinalIgnoreCase)
            )
            {
                await storage.DeleteAsync([asset.Key], ct);
                await db.Assets.Where(a => a.Id == id).ExecuteDeleteAsync(ct); // 0 rows is fine: a concurrent confirm already removed it
                return Invalid("file", "Uploaded file doesn't match the allowed type or size.");
            }

            asset.Status = AssetStatus.Ready;
            asset.Size = head.Value.Size;
            await db.SaveChangesAsync(ct);
        }

        return Results.Ok(
            new
            {
                id = asset.Id,
                url = storage.Url(asset.Key),
                contentType = asset.ContentType,
                size = asset.Size,
            }
        );
    }
}
