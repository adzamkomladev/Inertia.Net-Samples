/** Money helpers: the API speaks cents-long integers; "$" is applied on the client only. */

const formatter = new Intl.NumberFormat('en-US', {
  style: 'currency',
  currency: 'USD',
})

/** $12.00 from 1200 cents. Negative amounts keep the minus in front of the $. */
export function money(cents: number | null | undefined): string {
  const value = (cents ?? 0) / 100
  return value < 0 ? `-${formatter.format(-value)}` : formatter.format(value)
}

/** Compact amounts for KPI tiles and chart axes: $1.2k, $18.4k, $2.3M. */
export function moneyCompact(cents: number | null | undefined): string {
  const dollars = (cents ?? 0) / 100
  const sign = dollars < 0 ? '-' : ''
  const abs = Math.abs(dollars)
  if (abs >= 1_000_000) return `${sign}$${trim1(abs / 1_000_000)}M`
  if (abs >= 10_000) return `${sign}$${Math.round(abs / 1000)}k`
  if (abs >= 1_000) return `${sign}$${trim1(abs / 1000)}k`
  return `${sign}$${trim1(abs)}`
}

function trim1(value: number): string {
  const rounded = Math.round(value * 10) / 10
  return Number.isInteger(rounded) ? String(rounded) : rounded.toFixed(1)
}
