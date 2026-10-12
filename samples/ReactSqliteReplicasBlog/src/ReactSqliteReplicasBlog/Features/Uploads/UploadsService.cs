using ReactSqliteReplicasBlog.Data;
using ReactSqliteReplicasBlog.Infrastructure;
using static ReactSqliteReplicasBlog.Features.Uploads.UploadsHelpers;

namespace ReactSqliteReplicasBlog.Features.Uploads;

public static partial class UploadsService
{
    internal static async Task<IResult> UploadAsync(
        HttpContext ctx,
        IFormFile? file,
        AppDb db,
        Storage storage,
        TimeProvider clock,
        CancellationToken ct
    )
    {
        if (file is null || file.Length == 0)
            return Results.BadRequest(new { error = "Choose an image to upload." });
        if (file.Length > MaxBytes)
            return Results.BadRequest(new { error = "Images must be 5 MB or smaller." });
        var head = new byte[12];
        await using var stream = file.OpenReadStream();
        var read = await stream.ReadAtLeastAsync(head, head.Length, throwOnEndOfStream: false, ct);
        if (ImageSniffer.Detect(head.AsSpan(0, read)) is not var (contentType, ext))
            return Results.BadRequest(
                new { error = "Only PNG, JPEG, GIF and WebP images are allowed." }
            );
        stream.Position = 0;
        var now = clock.GetUtcNow().UtcDateTime;
        var key = $"uploads/{now:yyyy}/{now:MM}/{Guid.NewGuid():N}.{ext}";
        await storage.UploadMediaAsync(key, stream, contentType, ct);
        // IsAvatar is set when the profile is saved, so abandoned avatar uploads are still cleaned up.
        var upload = new Upload
        {
            OwnerId = ctx.User.UserId()!.Value,
            Key = key,
            Url = storage.MediaUrl(key),
            ContentType = contentType,
            Size = file.Length,
            CreatedAt = now,
        };
        db.Uploads.Add(upload);
        await db.SaveChangesAsync(ct);
        return Results.Ok(new { id = upload.Id, url = upload.Url });
    }
}
