using System.ComponentModel.DataAnnotations;
using MongoDB.Driver;
using CarParker.Data;
using CarParker.Infrastructure;

namespace CarParker.Features;

public sealed class PresignInput
{
    [Required] public string? ContentType { get; set; }
    [Range(1, long.MaxValue)] public long Size { get; set; }
    public string? FileName { get; set; }
}

/// <summary>Field problems (a 422 payload) or the presigned PUT details for the browser upload.</summary>
public sealed record PresignResult(Dictionary<string, string[]>? Problems = null, Guid AssetId = default, string? Url = null, string? ContentType = null);

/// <summary>Not found, field problems (a 422 payload), or the confirmed asset details.</summary>
public sealed record ConfirmResult(bool NotFound = false, Dictionary<string, string[]>? Problems = null, Guid Id = default, string? Url = null, string? ContentType = null, long Size = 0);
