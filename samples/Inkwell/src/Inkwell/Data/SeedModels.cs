using Microsoft.EntityFrameworkCore;

namespace Inkwell.Data;

public sealed class SeedCheckpoint
{
    public required string Name { get; set; }
    public int Completed { get; set; }
    public int Seed { get; set; }
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
