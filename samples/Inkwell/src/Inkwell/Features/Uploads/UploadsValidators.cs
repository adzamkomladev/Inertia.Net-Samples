using Inkwell.Data;
using Inkwell.Infrastructure;

namespace Inkwell.Features.Uploads;

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
