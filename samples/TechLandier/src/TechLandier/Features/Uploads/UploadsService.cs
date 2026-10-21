using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;
using TechLandier.Data;
using TechLandier.Infrastructure;

namespace TechLandier.Features;

public sealed class PresignInput
{
    [Required]
    public string? ContentType { get; set; }

    [Range(1, long.MaxValue)]
    public long Size { get; set; }
    public string? FileName { get; set; }
}

/// Browser uploads go straight to object storage with a presigned PUT; confirm checks what actually arrived.
/// The endpoints are JSON, so the results are plain HTTP results rather than SaveResults.
public sealed class UploadsService(AppDb db, Storage storage)
{
    const long Mb = 1024 * 1024;

    // content type -> extension and max size
    static readonly Dictionary<string, (string Ext, long Max)> Allowed = new(StringComparer.OrdinalIgnoreCase)
    {
        ["image/png"] = (".png", 5 * Mb),
        ["image/jpeg"] = (".jpg", 5 * Mb),
        ["image/webp"] = (".webp", 5 * Mb),
        ["image/gif"] = (".gif", 5 * Mb),
        ["application/pdf"] = (".pdf", 20 * Mb),
    };

    static IResult Invalid(string field, string message) =>
        Results.ValidationProblem(new Dictionary<string, string[]> { [field] = [message] }, statusCode: StatusCodes.Status422UnprocessableEntity);

    public async Task<IResult> PresignAsync(PresignInput input, Guid userId, CancellationToken ct)
    {
        var contentType = (input.ContentType ?? "").Trim().ToLowerInvariant();
        if (!Allowed.TryGetValue(contentType, out var rule))
            return Invalid("contentType", "Only PNG, JPEG, WebP, GIF images and PDFs are allowed.");
        if (input.Size > rule.Max) return Invalid("size", $"File is too large (max {rule.Max / Mb} MB).");

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
        return Results.Ok(new
        {
            assetId = asset.Id,
            url = storage.PresignPut(asset.Key, contentType, TimeSpan.FromMinutes(2)),
            headers = new Dictionary<string, string> { ["Content-Type"] = contentType },
        });
    }

    public async Task<IResult> ConfirmAsync(Guid id, Guid userId, CancellationToken ct)
    {
        var asset = await db.Assets.FirstOrDefaultAsync(a => a.Id == id && a.OwnerId == userId, ct);
        if (asset is null) return Results.NotFound();
        if (asset.Status != AssetStatus.Ready)
        {
            if (await storage.HeadAsync(asset.Key, ct) is not { } head) return Invalid("file", "Upload not found");
            var max = Allowed.TryGetValue(asset.ContentType, out var rule) ? rule.Max : 0;
            if (head.Size > max || !string.Equals(head.ContentType?.Split(';')[0].Trim(), asset.ContentType, StringComparison.OrdinalIgnoreCase))
            {
                await storage.DeleteAsync([asset.Key], ct);
                await db.Assets.Where(a => a.Id == id).ExecuteDeleteAsync(ct); // 0 rows is fine: a concurrent confirm removed it
                return Invalid("file", "Uploaded file doesn't match the allowed type or size.");
            }
            asset.Status = AssetStatus.Ready;
            asset.Size = head.Size;
            await db.SaveChangesAsync(ct);
        }
        return Results.Ok(new { id = asset.Id, url = storage.Url(asset.Key), contentType = asset.ContentType, size = asset.Size });
    }
}
