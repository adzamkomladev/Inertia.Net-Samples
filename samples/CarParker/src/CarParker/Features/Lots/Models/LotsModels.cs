using Inertia.Net;
using MongoDB.Driver;
using CarParker.Data;
using CarParker.Features.Bookings;

namespace CarParker.Features.Lots;

public sealed record LotCard(
    Guid Id, string Name, string Address, string Area, int LevelCount, long RatePerHourCents, long DailyMaxCents,
    string? ContactPhone, int Spots, int Free, double OccupancyPct, string[] SpotTypes, bool InsuranceCovered, long CoverageCents);

public sealed record SpotView(Guid Id, string Label, string Type, int Level, string State, DateTime? LastCheckpointAt, string? Note);

public sealed record Tariff(long RatePerHourCents, long DailyMaxCents, int InsurancePercent, long InsuranceMinCents, long CoverageCents);

public sealed record DayAvailability(string Date, int Free, int Total);

public sealed record LotOption(Guid Id, string Name);

public sealed record LotsPage(LotCard[] Data, int Page, bool HasMore) : IProvidesScrollMetadata
{
    public ScrollMetadata GetScrollMetadata() => ScrollMetadata.FromPage(Page, HasMore);
}
