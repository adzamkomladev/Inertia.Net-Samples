/** "$1,234.56" — matches the backend's Pricing.Show formatting. */
export function money(amount: number | null | undefined): string {
  const value = Number(amount ?? 0)
  return '$' + value.toLocaleString('en-US', { minimumFractionDigits: 2, maximumFractionDigits: 2 })
}

/** "$1,234" — whole dollars, for KPI tiles where cents are noise. */
export function moneyWhole(amount: number | null | undefined): string {
  const value = Number(amount ?? 0)
  return '$' + Math.round(value).toLocaleString('en-US')
}
