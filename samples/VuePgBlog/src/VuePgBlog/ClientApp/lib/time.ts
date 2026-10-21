/** Client-side date formatting: the API sends ISO instants and yyyy-MM-dd dates. */

const PAGES = Intl.DateTimeFormat('en-US', { month: 'short', day: 'numeric', timeZone: 'UTC' })
const FULL = Intl.DateTimeFormat('en-US', { month: 'short', day: 'numeric', year: 'numeric', timeZone: 'UTC' })
const TIME = Intl.DateTimeFormat('en-US', { hour: '2-digit', minute: '2-digit', hour12: false, timeZone: 'UTC' })

/** Short date: "Mar 14" (or "Mar 14, 2026" with the year). Input may be a date or instant. */
export function fmtDate(iso: string | null | undefined, withYear = false): string {
  if (!iso) return '—'
  const d = new Date(iso.length <= 10 ? `${iso}T00:00:00Z` : iso)
  return Number.isNaN(d.getTime()) ? '—' : (withYear ? FULL : PAGES).format(d)
}

/** UTC time part: "14:03". */
export function fmtTime(iso: string | null | undefined): string {
  if (!iso) return '—'
  return TIME.format(new Date(iso))
}

/** Date + UTC time. */
export function fmtDateTime(iso: string | null | undefined): string {
  if (!iso) return '—'
  const d = new Date(iso)
  return Number.isNaN(d.getTime()) ? '—' : `${FULL.format(d)} ${TIME.format(d)}`
}

/** "Mar 14 · 08:00–18:00" window helper for recurring rows. */
export function fmtWindow(from: string | null | undefined, to: string | null | undefined): string {
  if (!from || !to) return '—'
  return `${fmtDate(from)} – ${fmtDate(to)}`
}
