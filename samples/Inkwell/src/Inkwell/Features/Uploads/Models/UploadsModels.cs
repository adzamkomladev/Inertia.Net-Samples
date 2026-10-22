using Inkwell.Data;
using Inkwell.Infrastructure;

namespace Inkwell.Features.Uploads;

/// <summary>A stored upload, or a user-facing validation message.</summary>
public sealed record UploadResult(Guid? Id, string? Url, string? Error);
