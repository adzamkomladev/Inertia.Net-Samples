using ReactSqliteBlog.Data;
using ReactSqliteBlog.Infrastructure;
using static ReactSqliteBlog.Features.Uploads.UploadsHelpers;

namespace ReactSqliteBlog.Features.Uploads;

public static partial class UploadsService
{
    /// <summary>A stored upload, or a user-facing validation message.</summary>
    public sealed record UploadResult(Guid? Id, string? Url, string? Error);

    internal static async Task<UploadResult> UploadAsync(
        Guid userId,
        Stream? stream,
        long length,
        AppDb db,
        Storage storage,
        TimeProvider clock,
        CancellationToken ct
    )
    {
        if (stream is null || length == 0)
            return new UploadResult(null, null, "Choose an image to upload.");
        if (length > MaxBytes)
            return new UploadResult(null, null, "Images must be 5 MB or smaller.");
        var head = new byte[12];
        var read = await stream.ReadAtLeastAsync(head, head.Length, throwOnEndOfStream: false, ct);
        if (ImageSniffer.Detect(head.AsSpan(0, read)) is not var (contentType, ext))
            return new UploadResult(
                null,
                null,
                "Only PNG, JPEG, GIF and WebP images are allowed."
            );
        stream.Position = 0;
        var now = clock.GetUtcNow().UtcDateTime;
        var key = $"uploads/{now:yyyy}/{now:MM}/{Guid.NewGuid():N}.{ext}";
        await storage.UploadMediaAsync(key, stream, contentType, ct);
        // IsAvatar is set when the profile is saved, so abandoned avatar uploads are still cleaned up.
        var upload = new Upload
        {
            OwnerId = userId,
            Key = key,
            Url = storage.MediaUrl(key),
            ContentType = contentType,
            Size = length,
            CreatedAt = now,
        };
        db.Uploads.Add(upload);
        await db.SaveChangesAsync(ct);
        return new UploadResult(upload.Id, upload.Url, null);
    }
}
