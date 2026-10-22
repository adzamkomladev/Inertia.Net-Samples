import type { Tariff } from '@/types'

/**
 * Client mirror of the server's PricingRules: whole hours (minimum one), the daily max
 * applied per 24h slice, then the Spot Cover premium (percent of parking, with a minimum).
 */
export type WindowQuote = { hours: number; cents: number; uncappedCents: number; capped: boolean }

export function quoteWindow(tariff: Tariff, startMinutes: number, endMinutes: number): WindowQuote {
  const hours = Math.max(1, Math.ceil(Math.ceil(endMinutes - startMinutes) / 60))
  let remaining = hours
  let cents = 0
  while (remaining > 0) {
    const slice = Math.min(24, remaining)
    const sliceCents = slice * tariff.ratePerHourCents
    cents += tariff.dailyMaxCents > 0 && sliceCents > tariff.dailyMaxCents ? tariff.dailyMaxCents : sliceCents
    remaining -= slice
  }
  return { hours, cents, uncappedCents: hours * tariff.ratePerHourCents, capped: cents < hours * tariff.ratePerHourCents }
}

export function coverPremium(tariff: Tariff, parkingCents: number): number {
  return Math.max(tariff.insuranceMinCents, Math.round((parkingCents * tariff.insurancePercent) / 100))
}

export const toMinutes = (time: string): number => {
  const [h, m] = time.split(':').map(Number)
  return (h ?? 0) * 60 + (m ?? 0)
}
