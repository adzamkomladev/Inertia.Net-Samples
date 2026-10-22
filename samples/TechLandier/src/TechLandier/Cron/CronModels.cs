
using Microsoft.EntityFrameworkCore;
using TechLandier.Features;

namespace TechLandier.Data;

public sealed class CronLease
{
    public required string Name { get; set; }
    public DateTime? LastRunAt { get; set; }
    public DateTime? LockedUntil { get; set; }
}
