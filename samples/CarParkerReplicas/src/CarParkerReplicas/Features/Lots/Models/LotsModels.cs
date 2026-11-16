using Inertia.Net;
using MongoDB.Driver;
using CarParker.Data;
using CarParker.Features.Bookings;

namespace CarParker.Features.Lots;

public sealed record LotCard(
    Guid Id, string Name, string Address, string Area, int LevelCount, long RatePerHourCents, long DailyMaxCents,
    string? ContactPhone, int Spots, int Free, double OccupancyPct, string[] SpotTypes, bool InsuranceCovered, long CoverageCents, bool IsActive = true, bool Open24Hours = true, string Opens = "06:00", string Closes = "22:00", string TimeZoneId = "UTC", string? ArrivalNotes = null);

public sealed record SpotView(Guid Id, string Label, string Type, int Level, string State, DateTime? LastCheckpointAt, string? Note);

public sealed record Tariff(long RatePerHourCents, long DailyMaxCents, int InsurancePercent, long InsuranceMinCents, long CoverageCents);

public sealed record DayAvailability(string Date, int Free, int Total);

public sealed record LotOption(Guid Id, string Name);

// Page records: the component name is declared once and the generated TypeScript type follows these props.
public sealed record HomePage(LotCard[] Lots) : IInertiaPage
{
    public static string Component => "Home";
}

public sealed record LotsFilter(string Q, string Type, long? MaxPrice, string Sort);

public sealed record LotsIndexPage(
    [property: InertiaPropType(typeof(Page<LotCard>))] InertiaProp Lots,
    LotsFilter Filters) : IInertiaPage
{
    public static string Component => "lots/Index";
}

public sealed record LotShowPage(LotCard Lot, SpotView[] Spots, Tariff Tariff, DayAvailability[] Calendar) : IInertiaPage
{
    public static string Component => "lots/Show";
}

public sealed record AboutPage(string Doc) : IInertiaPage
{
    public static string Component => "about/Policy";
}
