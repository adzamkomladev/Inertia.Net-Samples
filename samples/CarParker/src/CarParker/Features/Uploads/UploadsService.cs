using System.ComponentModel.DataAnnotations;
using MongoDB.Driver;
using CarParker.Data;
using CarParker.Infrastructure;

namespace CarParker.Features;

public sealed class UploadsService(AppDb db, Storage storage, JobQueue jobs)
{
    public async Task<PresignResult> PresignAsync(PresignInput input, Guid userId, CancellationToken ct)
    {
        if (UploadsValidators.Validate(input) is { } problems) return new(Problems: problems);
        var contentType = input.ContentType!.Trim().ToLowerInvariant();
        if (!UploadsValidators.Allowed.TryGetValue(contentType, out var rule))
            return new PresignResult(Problems: UploadsValidators.Problem("contentType", "Only PNG, JPEG, WebP, GIF images and PDFs are allowed."));
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
        return new PresignResult(AssetId: asset.Id, Url: storage.PresignPut(asset.Key, contentType, TimeSpan.FromMinutes(2)), ContentType: contentType);
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
            if (head.Value.Size > max || !string.Equals(actualType, asset.ContentType, StringComparison.OrdinalIgnoreCase))
            {
                await db.TransactionAsync(async session =>
                {
                    await jobs.EnqueueAsync(db, new DeleteAssets([asset.Key]), session, ct: ct);
                    await db.Assets.DeleteOneAsync(session, a => a.Id == id && a.OwnerId == userId, cancellationToken: ct);
                }, ct);
                jobs.Wake();
                return new ConfirmResult(Problems: UploadsValidators.Problem("file", "Uploaded file does not match the allowed type or size."));
            }

            asset.Size = head.Value.Size;
            var updated = await db.Assets.UpdateOneAsync(a => a.Id == id && a.OwnerId == userId,
                Builders<Asset>.Update.Set(a => a.Status, AssetStatus.Ready).Set(a => a.Size, asset.Size), cancellationToken: ct);
            if (updated.MatchedCount == 0) return new ConfirmResult(NotFound: true);
        }
        return new ConfirmResult(Id: asset.Id, Url: storage.Url(asset.Key), ContentType: asset.ContentType, Size: asset.Size);
    }
}
