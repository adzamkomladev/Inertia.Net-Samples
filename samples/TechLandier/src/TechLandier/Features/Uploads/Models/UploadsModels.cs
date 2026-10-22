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

public sealed record PresignResult(Dictionary<string, string[]>? Problems = null, Guid AssetId = default, string? Url = null, string? ContentType = null);
public sealed record ConfirmResult(bool NotFound = false, Dictionary<string, string[]>? Problems = null, Guid Id = default, string? Url = null, string? ContentType = null, long Size = 0);
