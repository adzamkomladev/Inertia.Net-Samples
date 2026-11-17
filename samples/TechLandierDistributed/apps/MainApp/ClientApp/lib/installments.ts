import type { InstallmentRow } from '@/types'

/** Mirrors the backend BNPL rules: 5% plan fee baked in, installments 14 days apart. */
export const PLAN_FEE = 0.05
export const PLAN_COUNTS = [3, 6, 12] as const
export const PLAN_INTERVAL_DAYS = 14

/** Rounds to cents half away from zero, like the server's MidpointRounding.AwayFromZero. toPrecision drops float noise (33.495 * 100 is 3349.4999…). */
export const round2 = (n: number) => (Math.sign(n) * Math.round(Number((Math.abs(n) * 100).toPrecision(12)))) / 100

export const planTotal = (amount: number) => round2(amount * (1 + PLAN_FEE))
export const planFee = (amount: number) => round2(amount * PLAN_FEE)
export const perInstallment = (amount: number, count: number) => round2(planTotal(amount) / count)

/** Due date (yyyy-MM-dd) of installment `n` (1-based) for a plan started on `startIso`; the server counts 14 days per step from the order day. */
export function dueDateOf(n: number, startIso = new Date().toISOString().slice(0, 10)): string {
  const d = new Date(`${startIso}T00:00:00Z`)
  d.setUTCDate(d.getUTCDate() + PLAN_INTERVAL_DAYS * n)
  return d.toISOString().slice(0, 10)
}

const settled = (i: Pick<InstallmentRow, 'status'>) => i.status === 'paid' || i.status === 'waived'

/** The installment a customer pays next: the first one that is not settled (overdue ones come first by number). */
export const nextInstallment = (rows: InstallmentRow[]): InstallmentRow | null =>
  [...rows].sort((a, b) => a.number - b.number).find((i) => !settled(i)) ?? null

/** Display status of a row: scheduled rows after the next one read "upcoming" instead of "due". */
export function rowStatus(row: InstallmentRow, next: InstallmentRow | null): string {
  return row.status === 'due' && next && row.id !== next.id ? 'upcoming' : row.status
}
