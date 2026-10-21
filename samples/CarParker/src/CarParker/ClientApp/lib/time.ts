/** Deterministic date/time formatting (UTC for absolute stamps — the API speaks UTC — and local where noted). */

const dateFmt = new Intl.DateTimeFormat('en-US', { month: 'short', day: 'numeric', timeZone: 'UTC' })
const dateYearFmt = new Intl.DateTimeFormat('en-US', { month: 'short', day: 'numeric', year: 'numeric', timeZone: 'UTC' })
const timeFmt = new Intl.DateTimeFormat('en-US', { hour: '2-digit', minute: '2-digit', hour12: false, timeZone: 'UTC' })
const weekdayFmt = new Intl.DateTimeFormat('en-US', { weekday: 'short', timeZone: 'UTC' })

export function fmtDate(iso: string | null | undefined, withYear = false): string {
  if (!iso) return '—'
  const date = new Date(iso)
  if (Number.isNaN(date.getTime())) return '—'
  return (withYear ? dateYearFmt : dateFmt).format(date)
}

export function fmtTime(iso: string | null | undefined): string {
  if (!iso) return '—'
  const date = new Date(iso)
  return Number.isNaN(date.getTime()) ? '—' : timeFmt.format(date)
}

export function fmtDateTime(iso: string | null | undefined): string {
  if (!iso) return '—'
  return `${fmtDate(iso, true)} · ${fmtTime(iso)}`
}

/** "Sat 21 Oct, 14:00–18:00" for booking windows (all UTC, matching the backend contract). */
export function fmtWindow(start: string | null | undefined, end: string | null | undefined): string {
  if (!start || !end) return '—'
  return `${weekdayFmt.format(new Date(start))} ${fmtDate(start)}, ${fmtTime(start)}–${fmtTime(end)}`
}

/** "Oct 21 – Oct 28" for ranges; a one-line summary of a recurring booking's span. */
export function fmtRange(start: string | null | undefined, end: string | null | undefined): string {
  if (!start || !end) return '—'
  return `${fmtDate(start)} – ${fmtDate(end, true)}`
}

/** "14:00–18:00" from a pair of HH:mm strings. */
export function fmtDayWindow(timeFrom: string, timeTo: string): string {
  return `${timeFrom}–${timeTo}`
}
