using Inkwell.Data;
using Inkwell.Infrastructure;

namespace Inkwell.Features.Uploads;

/// <summary>A stored upload, or a user-facing validation message.</summary>
public sealed record UploadResult(Guid? Id, string? Url, string? Error);

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

/// <summary>Identifies an image by its leading bytes; never trust the client's content type or file name.</summary>
public static class ImageSniffer
{
    // Byte arrays, not u8 literals: "\x89"u8 is U+0089 encoded as two UTF-8 bytes (C2 89), not 0x89.
    static ReadOnlySpan<byte> Png => [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
    static ReadOnlySpan<byte> Jpeg => [0xFF, 0xD8, 0xFF];

    public static (string ContentType, string Ext)? Detect(ReadOnlySpan<byte> b)
    {
        if (b.StartsWith(Png)) return ("image/png", "png");
        if (b.StartsWith(Jpeg)) return ("image/jpeg", "jpg");
        if (b.StartsWith("GIF87a"u8) || b.StartsWith("GIF89a"u8)) return ("image/gif", "gif");
        if (b.Length >= 12 && b[..4].SequenceEqual("RIFF"u8) && b.Slice(8, 4).SequenceEqual("WEBP"u8)) return ("image/webp", "webp");
        return null;
    }
}
