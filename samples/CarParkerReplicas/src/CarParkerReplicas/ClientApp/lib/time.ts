/**
 * Deterministic date/time formatting. The API speaks UTC instants; drivers read and enter times on the lot's clock, so
 * every formatter takes an IANA zone (default UTC). Output depends only on the arguments, so SSR and hydration agree.
 */

const formatters = new Map<string, Intl.DateTimeFormat>()
function fmt(tz: string, options: Intl.DateTimeFormatOptions): Intl.DateTimeFormat {
  const key = `${tz}|${JSON.stringify(options)}`
  let formatter = formatters.get(key)
  if (!formatter) {
    formatter = new Intl.DateTimeFormat('en-US', { ...options, timeZone: tz })
    formatters.set(key, formatter)
  }
  return formatter
}

const parse = (iso: string | null | undefined): Date | null => {
  if (!iso) return null
  const date = new Date(iso)
  return Number.isNaN(date.getTime()) ? null : date
}

/** Wall-clock parts of an instant in a zone. */
export function zonedParts(date: Date, tz = 'UTC') {
  const parts = fmt(tz, {
    year: 'numeric', month: '2-digit', day: '2-digit', hour: '2-digit', minute: '2-digit', hourCycle: 'h23', weekday: 'short',
  }).formatToParts(date)
  const get = (type: string) => parts.find(p => p.type === type)?.value ?? ''
  return {
    date: `${get('year')}-${get('month')}-${get('day')}`,
    time: `${get('hour')}:${get('minute')}`,
    weekday: get('weekday'),
    day: Number(get('day')),
    month: fmt(tz, { month: 'short' }).format(date),
  }
}

export function fmtDate(iso: string | null | undefined, withYear = false, tz = 'UTC'): string {
  const date = parse(iso)
  if (!date) return '—'
  // Day before month ("14 Nov 2026"), the same order as fmtDay, so dates read alike across the app.
  const p = zonedParts(date, tz)
  return `${p.day} ${p.month}${withYear ? ` ${p.date.slice(0, 4)}` : ''}`
}

export function fmtTime(iso: string | null | undefined, tz = 'UTC'): string {
  const date = parse(iso)
  return date ? zonedParts(date, tz).time : '—'
}

export function fmtDateTime(iso: string | null | undefined, tz = 'UTC'): string {
  if (!parse(iso)) return '—'
  return `${fmtDate(iso, true, tz)} · ${fmtTime(iso, tz)}`
}

/** "Wed 18 Nov" in the zone. */
export function fmtDay(iso: string | null | undefined, tz = 'UTC'): string {
  const date = parse(iso)
  if (!date) return '—'
  const p = zonedParts(date, tz)
  return `${p.weekday} ${p.day} ${p.month}`
}

/** "Wed 18 Nov, 14:00" in the zone. */
export function fmtDayTime(iso: string | null | undefined, tz = 'UTC'): string {
  return parse(iso) ? `${fmtDay(iso, tz)}, ${fmtTime(iso, tz)}` : '—'
}

/** "Wed 18 Nov, 14:00–18:00"; a window that ends on another day names both days. */
export function fmtWindow(start: string | null | undefined, end: string | null | undefined, tz = 'UTC'): string {
  const from = parse(start)
  const to = parse(end)
  if (!from || !to) return '—'
  const a = zonedParts(from, tz)
  const b = zonedParts(to, tz)
  return a.date === b.date ? `${fmtDay(start, tz)}, ${a.time}–${b.time}` : `${fmtDayTime(start, tz)} – ${fmtDayTime(end, tz)}`
}

/** "Oct 21 – Oct 28, 2026" for ranges; a one-line summary of a recurring booking's span. */
export function fmtRange(start: string | null | undefined, end: string | null | undefined, tz = 'UTC'): string {
  if (!parse(start) || !parse(end)) return '—'
  return `${fmtDate(start, false, tz)} – ${fmtDate(end, true, tz)}`
}

/** "14:00–18:00" from a pair of HH:mm strings. */
export function fmtDayWindow(timeFrom: string, timeTo: string): string {
  return `${timeFrom}–${timeTo}`
}

/** "New York time (EST)" for America/New_York at that instant, "UTC" for UTC. */
export function zoneLabel(tz: string, at: Date | string = new Date()): string {
  if (!tz || tz === 'UTC' || tz === 'Etc/UTC') return 'UTC'
  const date = typeof at === 'string' ? (parse(at) ?? new Date()) : at
  const city = (tz.split('/').pop() ?? tz).replace(/_/g, ' ')
  const short = fmt(tz, { timeZoneName: 'short' }).formatToParts(date).find(p => p.type === 'timeZoneName')?.value
  return short ? `${city} time (${short})` : `${city} time`
}

// ---------- lot-local calendar arithmetic (yyyy-MM-dd strings, no zone) ----------

/** Today's date on the lot's clock. */
export const zonedToday = (tz: string, now: number = Date.now()): string => zonedParts(new Date(now), tz).date

export const addDays = (date: string, days: number): string =>
  new Date(Date.parse(`${date}T00:00:00Z`) + days * 86_400_000).toISOString().slice(0, 10)

/** .NET day-of-week number (Sunday = 0) of a calendar date. */
export const weekdayOf = (date: string): number => new Date(`${date}T00:00:00Z`).getUTCDay()

/**
 * The UTC instant of a lot-local date and HH:mm, mirroring the server's LotTime.ToUtc: a time skipped by a
 * spring-forward gap moves on by the gap's real length (30 minutes on Lord Howe, one hour in New York), and a
 * time that happens twice on a fall-back night is the later, standard-time instant (01:30 in New York on
 * 1 Nov 2026 is 06:30Z, not 05:30Z).
 */
export function zonedToUtc(date: string, time: string, tz: string): Date {
  const DAY = 86_400_000
  /** The zone's UTC offset in ms at an instant. */
  const offsetAt = (instant: number) => {
    const p = zonedParts(new Date(instant), tz)
    return Date.parse(`${p.date}T${p.time}:00Z`) - Math.floor(instant / 60_000) * 60_000
  }
  const resolve = (wall: number): number => {
    // Offsets in force around this wall time (a transition has at most two); each gives one candidate instant.
    const before = offsetAt(wall - 1.5 * DAY)
    const after = offsetAt(wall + 1.5 * DAY)
    const candidates = [...new Set([before, after])].map(offset => wall - offset).filter(instant => offsetAt(instant) === wall - instant)
    if (candidates.length) return Math.max(...candidates)
    // In a spring-forward gap no instant shows this wall time; move on by the gap, like the server.
    return resolve(wall + (after - before))
  }
  return new Date(resolve(Date.parse(`${date}T${time}:00Z`)))
}
