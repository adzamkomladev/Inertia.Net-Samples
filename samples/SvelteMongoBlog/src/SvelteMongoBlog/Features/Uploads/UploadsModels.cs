using System.ComponentModel.DataAnnotations;

namespace SvelteMongoBlog.Features;

public sealed class PresignInput
{
    [Required]
    public string? ContentType { get; set; }

    [Range(1, long.MaxValue)]
    public long Size { get; set; }
    public string? FileName { get; set; }
}
