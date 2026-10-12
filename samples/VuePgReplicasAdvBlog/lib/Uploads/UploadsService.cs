using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;
using VuePgReplicasAdvBlog.Data;
using VuePgReplicasAdvBlog.Infrastructure;
using static VuePgReplicasAdvBlog.Features.Uploads.UploadsHelpers;

namespace VuePgReplicasAdvBlog.Features.Uploads;

public static class UploadsService
{
    public static async Task<PresignedUpload> PresignAsync(
        PresignInput input,
        Guid userId,
        AppDb db,
        Storage storage,
        CancellationToken ct
    )
    {
        var results = new List<ValidationResult>();
        if (!Validator.TryValidateObject(input, new ValidationContext(input), results, true))
            throw new FeatureException(FailureKind.Validation, errors:
                results
                    .SelectMany(r =>
                        (r.MemberNames.Any() ? r.MemberNames : [""]).Select(m =>
                            (m, r.ErrorMessage ?? "Invalid.")
                        )
                    )
                    .GroupBy(x => x.m)
                    .ToDictionary(g => g.Key, g => g.Select(x => x.Item2).ToArray())
            );
        var contentType = input.ContentType!.Trim().ToLowerInvariant();
        if (!Allowed.TryGetValue(contentType, out var rule))
            throw FeatureException.Invalid("contentType", "Only PNG, JPEG, WebP, GIF images and PDFs are allowed.");
        if (input.Size > rule.Max)
            throw FeatureException.Invalid("size", $"File is too large (max {rule.Max / Mb} MB).");
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
        return new PresignedUpload(asset.Id, storage.PresignPut(key, contentType, TimeSpan.FromMinutes(2)), new Dictionary<string, string> { ["Content-Type"] = contentType });
    }

    public static async Task<ConfirmedUpload> ConfirmAsync(
        Guid id,
        Guid userId,
        AppDb db,
        Storage storage,
        CancellationToken ct
    )
    {
        var asset = await db.Assets.FirstOrDefaultAsync(a => a.Id == id && a.OwnerId == userId, ct);
        if (asset is null)
            throw new FeatureException(FailureKind.Missing);
        if (asset.Status != AssetStatus.Ready)
        {
            var head = await storage.HeadAsync(asset.Key, ct);
            if (head is null)
                throw FeatureException.Invalid("file", "Upload not found");
            var max = Allowed.TryGetValue(asset.ContentType, out var rule) ? rule.Max : 0;
            var actualType = head.Value.ContentType?.Split(';')[0].Trim();
            if (
                head.Value.Size > max
                || !string.Equals(actualType, asset.ContentType, StringComparison.OrdinalIgnoreCase)
            )
            {
                await storage.DeleteAsync([asset.Key], ct);
                await db.Assets.Where(a => a.Id == id).ExecuteDeleteAsync(ct); // 0 rows is fine: a concurrent confirm already removed it
                throw FeatureException.Invalid("file", "Uploaded file doesn't match the allowed type or size.");
            }

            asset.Status = AssetStatus.Ready;
            asset.Size = head.Value.Size;
            await db.SaveChangesAsync(ct);
        }

        return new ConfirmedUpload(asset.Id, storage.Url(asset.Key), asset.ContentType, asset.Size);
    }
}
