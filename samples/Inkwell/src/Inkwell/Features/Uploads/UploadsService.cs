using Inkwell.Data;
using Inkwell.Infrastructure;

namespace Inkwell.Features.Uploads;

public sealed class UploadsService(AppDb db, Storage storage, TimeProvider time)
{
    public const long MaxBytes = 5 * 1024 * 1024;

    public async Task<UploadResult> UploadAsync(Guid userId, Stream? stream, long length, CancellationToken ct)
    {
        if (stream is null || length == 0) return Fail("Choose an image to upload.");
        if (length > MaxBytes) return Fail("Images must be 5 MB or smaller.");
        var head = new byte[12];
        var read = await stream.ReadAtLeastAsync(head, head.Length, throwOnEndOfStream: false, ct);
        if (ImageSniffer.Detect(head.AsSpan(0, read)) is not var (contentType, ext))
            return Fail("Only PNG, JPEG, GIF and WebP images are allowed.");
        stream.Position = 0;
        var now = time.GetUtcNow().UtcDateTime;
        var key = $"uploads/{now:yyyy}/{now:MM}/{Guid.NewGuid():N}.{ext}";
        await storage.UploadMediaAsync(key, stream, contentType, ct);
        // IsAvatar is set when the profile is saved, so abandoned avatar uploads are still cleaned up.
        var upload = new Upload { OwnerId = userId, Key = key, Url = storage.MediaUrl(key), ContentType = contentType, Size = length, CreatedAt = now };
        db.Uploads.Add(upload);
        await db.SaveChangesAsync(ct);
        return new UploadResult(upload.Id, upload.Url, null);
    }

    static UploadResult Fail(string error) => new(null, null, error);
}
