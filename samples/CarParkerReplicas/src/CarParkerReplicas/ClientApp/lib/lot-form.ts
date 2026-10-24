import type { LotCard } from '@/types'

/** Fields of POST /admin/lots and PUT /admin/lots/{id}; money stays in cents like the API. */
export type LotFields = {
  name: string
  address: string
  area: string
  levelCount: number
  ratePerHourCents: number
  dailyMaxCents: number
  contactPhone: string
  arrivalNotes: string
  isActive: boolean
  open24Hours: boolean
  opens: string
  closes: string
  timeZoneId: string
  coverageCents: number
}

export const lotFields = (lot?: LotCard): LotFields => ({
  name: lot?.name ?? '',
  address: lot?.address ?? '',
  area: lot?.area ?? '',
  levelCount: lot?.levelCount ?? 2,
  ratePerHourCents: lot?.ratePerHourCents ?? 400,
  dailyMaxCents: lot?.dailyMaxCents ?? 2400,
  contactPhone: lot?.contactPhone ?? '',
  arrivalNotes: lot?.arrivalNotes ?? '',
  isActive: lot?.isActive ?? true,
  open24Hours: lot?.open24Hours ?? true,
  opens: lot?.opens ?? '06:00',
  closes: lot?.closes ?? '22:00',
  timeZoneId: lot?.timeZoneId ?? 'UTC',
  coverageCents: lot?.coverageCents ?? 50_000,
})
