using System.ComponentModel.DataAnnotations;
using MongoDB.Driver;
using CarParker.Data;
using CarParker.Infrastructure;
using static CarParker.Features.UploadsHelpers;

namespace CarParker.Features;

public static partial class UploadsService
{
    /// <summary>Field problems (a 422 payload for the handler) or the presigned PUT details for the browser upload.</summary>
    public sealed record PresignResult(
        Dictionary<string, string[]>? Problems = null,
        Guid AssetId = default,
        string? Url = null,
        string? ContentType = null);

    /// <summary>Not found, field problems (a 422 payload), or the confirmed asset details.</summary>
    public sealed record ConfirmResult(
        bool NotFound = false,
        Dictionary<string, string[]>? Problems = null,
        Guid Id = default,
        string? Url = null,
        string? ContentType = null,
        long Size = 0);

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
                Problems: results
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
            return new PresignResult(Problems: Problem("contentType", "Only PNG, JPEG, WebP, GIF images and PDFs are allowed."));
        if (input.Size > rule.Max)
            return new PresignResult(Problems: Problem("size", $"File is too large (max {rule.Max / Mb} MB)."));
        var now = DateTime.UtcNow;
        var key = $"u/{userId}/{now:yyyy}/{now:MM}/{Guid.NewGuid():N}{rule.Ext}";
        var asset = new Asset
        {
            OwnerId = userId,
            Key = key,
            ContentType = contentType,
            Size = input.Size,
        };
        await db.Assets.InsertOneAsync(asset, cancellationToken: ct);
        // ponytail: after confirm, the still-valid presigned PUT (up to 2 min) could overwrite the confirmed object; upgrade path: copy to a final key on confirm.
        return new PresignResult(
            AssetId: asset.Id,
            Url: storage.PresignPut(key, contentType, TimeSpan.FromMinutes(2)),
            ContentType: contentType
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
        var asset = await db
            .Assets.Find(a => a.Id == id && a.OwnerId == userId)
            .FirstOrDefaultAsync(ct);
        if (asset is null)
            return new ConfirmResult(NotFound: true);
        if (asset.Status != AssetStatus.Ready)
        {
            var head = await storage.HeadAsync(asset.Key, ct);
            if (head is null)
                return new ConfirmResult(Problems: Problem("file", "Upload not found"));
            var max = Allowed.TryGetValue(asset.ContentType, out var rule) ? rule.Max : 0;
            var actualType = head.Value.ContentType?.Split(';')[0].Trim();
            if (
                head.Value.Size > max
                || !string.Equals(actualType, asset.ContentType, StringComparison.OrdinalIgnoreCase)
            )
            {
                await storage.DeleteAsync([asset.Key], ct);
                await db.Assets.DeleteOneAsync(a => a.Id == id && a.OwnerId == userId, ct); // 0 rows is fine: a concurrent confirm already removed it
                return new ConfirmResult(Problems: Problem("file", "Uploaded file doesn't match the allowed type or size."));
            }

            asset.Status = AssetStatus.Ready;
            asset.Size = head.Value.Size;
            var updated = await db.Assets.UpdateOneAsync(
                a => a.Id == id && a.OwnerId == userId,
                Builders<Asset>
                    .Update.Set(a => a.Status, AssetStatus.Ready)
                    .Set(a => a.Size, asset.Size),
                cancellationToken: ct
            );
            if (updated.MatchedCount == 0)
                return new ConfirmResult(NotFound: true);
        }

        return new ConfirmResult(
            Id: asset.Id,
            Url: storage.Url(asset.Key),
            ContentType: asset.ContentType,
            Size: asset.Size
        );
    }
}
