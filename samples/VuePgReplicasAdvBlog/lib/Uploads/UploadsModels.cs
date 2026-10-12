using System.ComponentModel.DataAnnotations;

namespace VuePgReplicasAdvBlog.Features.Uploads;

public sealed class PresignInput
{
    [Required]
    public string? ContentType { get; set; }

    [Range(1, long.MaxValue)]
    public long Size { get; set; }
    public string? FileName { get; set; }
}

public sealed record PresignedUpload(Guid AssetId, string Url, Dictionary<string, string> Headers);
public sealed record ConfirmedUpload(Guid Id, string Url, string ContentType, long Size);
