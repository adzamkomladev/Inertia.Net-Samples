using CarParker.Features.Bookings;

namespace CarParker.Features.Lots;

public static class LotsMappers
{
    public static List<LotOption> OptionsFor(IEnumerable<Lot> lots) => lots.Select(l => new LotOption(l.Id, l.Name)).ToList();

    public static Tariff TariffOf(Lot lot) => new(
        lot.RatePerHourCents, lot.DailyMaxCents, PricingRules.InsurancePercent, PricingRules.InsurancePremiumMinCents, lot.CoverageCents);
}
