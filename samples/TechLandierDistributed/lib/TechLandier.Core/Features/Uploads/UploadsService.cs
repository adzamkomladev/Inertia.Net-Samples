using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;
using TechLandier.Data;
using TechLandier.Infrastructure;

namespace TechLandier.Features;

/// Browser uploads go straight to object storage with a presigned PUT; confirm checks what actually arrived.
public sealed class UploadsService(AppDb db, Storage storage, JobQueue jobs)
{
    public async Task<PresignResult> PresignAsync(PresignInput input, Guid userId, CancellationToken ct)
    {
        var contentType = (input.ContentType ?? "").Trim().ToLowerInvariant();
        if (!UploadsValidators.Allowed.TryGetValue(contentType, out var rule))
            return new(Problems: UploadsValidators.Problem("contentType", "Only PNG, JPEG, WebP, GIF images and PDFs are allowed."));
        if (input.Size < 1 || input.Size > rule.Max) return new(Problems: UploadsValidators.Problem("size", $"Choose a file between 1 byte and {rule.Max / UploadsValidators.Mb} MB."));

        var now = DateTime.UtcNow;
        var asset = new Asset
        {
            OwnerId = userId,
            Key = $"u/{userId}/{now:yyyy}/{now:MM}/{Guid.NewGuid():N}{rule.Ext}",
            ContentType = contentType,
            Size = input.Size,
        };
        db.Assets.Add(asset);
        await db.SaveChangesAsync(ct);
        // ponytail: the presigned PUT stays valid for 2 minutes after confirm and could overwrite the object; copy to a final key on confirm if that matters.
        return new(AssetId: asset.Id, Url: storage.PresignPut(asset.Key, contentType, TimeSpan.FromMinutes(2)), ContentType: contentType);
    }

    public async Task<ConfirmResult> ConfirmAsync(Guid id, Guid userId, CancellationToken ct)
    {
        var asset = await db.Assets.FirstOrDefaultAsync(a => a.Id == id && a.OwnerId == userId, ct);
        if (asset is null) return new(NotFound: true);
        if (asset.Status != AssetStatus.Ready)
        {
            if (await storage.HeadAsync(asset.Key, ct) is not { } head) return new(Problems: UploadsValidators.Problem("file", "Upload not found"));
            var max = UploadsValidators.Allowed.TryGetValue(asset.ContentType, out var rule) ? rule.Max : 0;
            if (head.Size > max || !string.Equals(head.ContentType?.Split(';')[0].Trim(), asset.ContentType, StringComparison.OrdinalIgnoreCase))
            {
                await using var tx = await db.Database.BeginTransactionAsync(ct);
                jobs.Enqueue(db, new DeleteAssets([asset.Key]));
                await db.Assets.Where(a => a.Id == id).ExecuteDeleteAsync(ct); // 0 rows is fine: a concurrent confirm removed it
                await db.SaveChangesAsync(ct);
                await tx.CommitAsync(ct);
                jobs.Wake();
                return new(Problems: UploadsValidators.Problem("file", "Uploaded file doesn't match the allowed type or size."));
            }
            asset.Status = AssetStatus.Ready;
            asset.Size = head.Size;
            await db.SaveChangesAsync(ct);
        }
        return new(Id: asset.Id, Url: storage.Url(asset.Key), ContentType: asset.ContentType, Size: asset.Size);
    }
}
