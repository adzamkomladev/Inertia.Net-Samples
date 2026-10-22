using Microsoft.AspNetCore.Identity;

namespace Inkwell.Data;

public enum JobStatus
{
    Pending = 0,
    Running = 1,
    Done = 2,
    Failed = 3,
}

public sealed class Job
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    /// <summary>The payload type name, e.g. "SendVerificationEmail".</summary>
    public required string Type { get; set; }
    public required string Payload { get; set; }
    public JobStatus Status { get; set; }
    public int Attempts { get; set; }
    public DateTime RunAt { get; set; }
    public DateTime? LockedUntil { get; set; }
    public string? LastError { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
