using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;
using TechLandier.Data;
using TechLandier.Infrastructure;
using static TechLandier.Features.UploadsHelpers;

namespace TechLandier.Features;

public static partial class UploadsService
{
    /// <summary>A presigned upload target, or the validation errors the handler renders as a 422.</summary>
    public sealed record PresignResult(
        Dictionary<string, string[]>? Errors,
        Guid AssetId,
        string? Url,
        Dictionary<string, string>? Headers
    );

    /// <summary>A confirmed asset, a validation error, or a missing row.</summary>
    public sealed record ConfirmResult(
        Dictionary<string, string[]>? Errors,
        bool NotFound,
        Guid Id,
        string? Url,
        string? ContentType,
        long Size
    );

    internal static async Task<PresignResult> PresignAsync(
        PresignInput input,
        Guid userId,
        AppDb db,
        Storage storage,
        CancellationToken ct
    )
    {
        var results = new List<ValidationResult>();
        if (!Validator.TryValidateObject(input, new ValidationContext(input), results, true))
            return new PresignResult(
                results
                    .SelectMany(r =>
                        (r.MemberNames.Any() ? r.MemberNames : [""]).Select(m =>
                            (m, r.ErrorMessage ?? "Invalid.")
                        )
                    )
                    .GroupBy(x => x.m)
                    .ToDictionary(g => g.Key, g => g.Select(x => x.Item2).ToArray()),
                Guid.Empty,
                null,
                null
            );
        var contentType = input.ContentType!.Trim().ToLowerInvariant();
        if (!Allowed.TryGetValue(contentType, out var rule))
            return PresignInvalid(
                "contentType",
                "Only PNG, JPEG, WebP, GIF images and PDFs are allowed."
            );
        if (input.Size > rule.Max)
            return PresignInvalid("size", $"File is too large (max {rule.Max / Mb} MB).");
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
        return new PresignResult(
            null,
            asset.Id,
            storage.PresignPut(key, contentType, TimeSpan.FromMinutes(2)),
            new Dictionary<string, string> { ["Content-Type"] = contentType }
        );
    }

    internal static async Task<ConfirmResult> ConfirmAsync(
        Guid id,
        Guid userId,
        AppDb db,
        Storage storage,
        CancellationToken ct
    )
    {
        var asset = await db.Assets.FirstOrDefaultAsync(a => a.Id == id && a.OwnerId == userId, ct);
        if (asset is null)
            return new ConfirmResult(null, true, Guid.Empty, null, null, 0);
        if (asset.Status != AssetStatus.Ready)
        {
            var head = await storage.HeadAsync(asset.Key, ct);
            if (head is null)
                return ConfirmInvalid("file", "Upload not found");
            var max = Allowed.TryGetValue(asset.ContentType, out var rule) ? rule.Max : 0;
            var actualType = head.Value.ContentType?.Split(';')[0].Trim();
            if (
                head.Value.Size > max
                || !string.Equals(actualType, asset.ContentType, StringComparison.OrdinalIgnoreCase)
            )
            {
                await storage.DeleteAsync([asset.Key], ct);
                await db.Assets.Where(a => a.Id == id).ExecuteDeleteAsync(ct); // 0 rows is fine: a concurrent confirm already removed it
                return ConfirmInvalid(
                    "file",
                    "Uploaded file doesn't match the allowed type or size."
                );
            }

            asset.Status = AssetStatus.Ready;
            asset.Size = head.Value.Size;
            await db.SaveChangesAsync(ct);
        }

        return new ConfirmResult(
            null,
            false,
            asset.Id,
            storage.Url(asset.Key),
            asset.ContentType,
            asset.Size
        );
    }

    static ConfirmResult ConfirmInvalid(string field, string message) =>
        new(
            new Dictionary<string, string[]> { [field] = [message] },
            false,
            Guid.Empty,
            null,
            null,
            0
        );

    static PresignResult PresignInvalid(string field, string message) =>
        new(
            new Dictionary<string, string[]> { [field] = [message] },
            Guid.Empty,
            null,
            null
        );
}
