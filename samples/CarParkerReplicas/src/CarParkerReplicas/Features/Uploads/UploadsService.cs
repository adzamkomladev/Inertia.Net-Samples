using System.ComponentModel.DataAnnotations;
using MongoDB.Driver;
using CarParker.Data;
using CarParker.Infrastructure;

namespace CarParker.Features;

public sealed class UploadsService(AppDb db, Storage storage, JobEnqueuer jobs)
{
    public async Task<PresignResult> PresignAsync(PresignInput input, Guid userId, CancellationToken ct)
    {
        if (UploadsValidators.Validate(input) is { } problems) return new(Problems: problems);
        var contentType = input.ContentType!.Trim().ToLowerInvariant();
        if (!UploadsValidators.Allowed.TryGetValue(contentType, out var rule))
            return new PresignResult(Problems: UploadsValidators.Problem("contentType", "Only PNG, JPEG, WebP and GIF images are allowed."));
        if (input.Size > rule.Max)
            return new PresignResult(Problems: UploadsValidators.Problem("size", $"File is too large (max {rule.Max / UploadsValidators.Mb} MB)."));

        var now = DateTime.UtcNow;
        var asset = new Asset
        {
            OwnerId = userId,
            Key = $"u/{userId}/{now:yyyy}/{now:MM}/{Guid.NewGuid():N}{rule.Ext}",
            ContentType = contentType,
            Size = input.Size,
        };
        await db.Assets.InsertOneAsync(asset, cancellationToken: ct);
        // ponytail: after confirm, the still-valid presigned PUT (up to 2 min) could overwrite the confirmed object; upgrade path: copy to a final key on confirm.
        return new PresignResult(AssetId: asset.Id, Url: storage.PresignPut(asset.Key, contentType, input.Size, TimeSpan.FromMinutes(2)), ContentType: contentType);
    }

    public async Task<ConfirmResult> ConfirmAsync(Guid id, Guid userId, CancellationToken ct)
    {
        var asset = await db.Assets.Find(a => a.Id == id && a.OwnerId == userId).FirstOrDefaultAsync(ct);
        if (asset is null) return new ConfirmResult(NotFound: true);
        if (asset.Status != AssetStatus.Ready)
        {
            var head = await storage.HeadAsync(asset.Key, ct);
            if (head is null) return new ConfirmResult(Problems: UploadsValidators.Problem("file", "Upload not found"));
            var max = UploadsValidators.Allowed.TryGetValue(asset.ContentType, out var rule) ? rule.Max : 0;
            var actualType = head.Value.ContentType?.Split(';')[0].Trim();
            var sizeOk = head.Value.Size <= max && head.Value.Size == asset.Size;
            var typeOk = string.Equals(actualType, asset.ContentType, StringComparison.OrdinalIgnoreCase);
            // The declared type is client-controlled: the bytes themselves must carry that image's signature.
            var bytesOk = sizeOk && typeOk && UploadsValidators.MatchesSignature(asset.ContentType, await storage.ReadHeadAsync(asset.Key, 16, ct));
            if (!sizeOk || !typeOk || !bytesOk)
            {
                // Delete the object first (idempotent), then the row: a failure in between leaves a row the orphan cron retries.
                await jobs.DispatchAsync(new DeleteAssets([asset.Key]), ct);
                await db.Assets.DeleteOneAsync(a => a.Id == id && a.OwnerId == userId, cancellationToken: ct);
                return new ConfirmResult(Problems: UploadsValidators.Problem("file", "Uploaded file is not an allowed image of the declared type and size."));
            }

            asset.Size = head.Value.Size;
            var updated = await db.Assets.UpdateOneAsync(a => a.Id == id && a.OwnerId == userId,
                Builders<Asset>.Update.Set(a => a.Status, AssetStatus.Ready).Set(a => a.Size, asset.Size), cancellationToken: ct);
            if (updated.MatchedCount == 0) return new ConfirmResult(NotFound: true);
        }
        return new ConfirmResult(Id: asset.Id, Url: storage.Url(asset.Key), ContentType: asset.ContentType, Size: asset.Size);
    }
}
