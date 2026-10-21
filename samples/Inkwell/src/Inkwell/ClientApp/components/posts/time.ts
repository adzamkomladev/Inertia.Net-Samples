const rtf = new Intl.RelativeTimeFormat('en', { numeric: 'auto' })
const units: [Intl.RelativeTimeFormatUnit, number][] = [
  ['year', 31_536_000], ['month', 2_592_000], ['week', 604_800], ['day', 86_400], ['hour', 3600], ['minute', 60],
]

/** Server timestamps are UTC but may lack a zone suffix. */
export function parseUtc(iso: string) {
  return new Date(/[zZ]|[+-]\d\d:?\d\d$/.test(iso) ? iso : iso + 'Z')
}

export function timeAgo(iso: string) {
  const seconds = (parseUtc(iso).getTime() - Date.now()) / 1000
  for (const [unit, size] of units) {
    if (Math.abs(seconds) >= size) return rtf.format(Math.round(seconds / size), unit)
  }
  return 'just now'
}

export function fullDate(iso: string) {
  return parseUtc(iso).toLocaleDateString('en', { dateStyle: 'long' })
}

/** "Oct 17", with the year when it is not the current one. */
export function shortDate(iso: string) {
  const d = parseUtc(iso)
  return d.toLocaleDateString('en', { month: 'short', day: 'numeric', ...(d.getFullYear() === new Date().getFullYear() ? {} : { year: 'numeric' }) })
}

export const readMinutes = (body: string) => Math.max(1, Math.round(body.trim().split(/\s+/).length / 230))
