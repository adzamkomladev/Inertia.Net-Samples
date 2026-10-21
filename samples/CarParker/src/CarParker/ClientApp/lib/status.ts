/**
 * Status tone map for the seven-tone pill system (`StatusBadge`).
 *
 * Tones: mint = healthy/free, violet = booked/reserved, ink = occupied/strong-neutral,
 * amber = attention, red = failure/danger, grey = muted/blocked, navy = primary.
 * All values arrive lowercase from the C# `enum.ToString().ToLowerInvariant()` projections.
 */
export type Tone = 'mint' | 'violet' | 'ink' | 'amber' | 'red' | 'grey' | 'navy'

const TONES: Record<string, Tone> = {
  // spot / lot states
  free: 'mint',
  booked: 'violet',
  occupied: 'ink',
  blocked: 'grey',
  // reservations + windows
  confirmed: 'violet',
  completed: 'mint',
  cancelled: 'grey',
  single: 'navy',
  recurring: 'violet',
  // occurrences + sessions
  scheduled: 'navy',
  active: 'mint',
  done: 'grey',
  noshow: 'amber',
  inprogress: 'violet',
  exited: 'grey',
  // payments
  pending: 'amber',
  paid: 'mint',
  failed: 'red',
  refunded: 'violet',
  // refunds
  requested: 'amber',
  approved: 'violet',
  rejected: 'red',
  // Spot Cover policies + claims
  void: 'grey',
  submitted: 'amber',
  underreview: 'violet',
  // incidents (stored strings, guard sets these via the console)
  open: 'red',
  resolved: 'mint',
  // incident kinds / claim kinds / alert kinds → neutral info tone
  unauthorized_entry: 'amber',
  theft: 'red',
  damage: 'amber',
  patrol_note: 'navy',
  other: 'grey',
  brokenwindow: 'amber',
  expired: 'grey',
  review: 'amber',
}

/** Seven-tone classes keyed by tone; `StatusBadge` renders these verbatim. */
export const toneClasses: Record<Tone, string> = {
  mint: 'bg-spot-free/15 text-spot-free-foreground ring-spot-free/30',
  violet: 'bg-spot-booked/12 text-spot-booked ring-spot-booked/30',
  ink: 'bg-spot-occupied/10 text-foreground ring-spot-occupied/25',
  amber: 'bg-cta/18 text-cta-foreground ring-cta/40',
  red: 'bg-destructive/10 text-destructive ring-destructive/30',
  grey: 'bg-muted text-muted-foreground ring-border',
  navy: 'bg-primary/8 text-primary ring-primary/25 dark:bg-primary/15',
}

const dotClasses: Record<Tone, string> = {
  mint: 'bg-spot-free',
  violet: 'bg-spot-booked',
  ink: 'bg-spot-occupied',
  amber: 'bg-cta',
  red: 'bg-destructive',
  grey: 'bg-spot-blocked',
  navy: 'bg-primary',
}

/** The tone for a status value; unknown values fall back to a quiet neutral. */
export function statusTone(status: string | null | undefined): Tone {
  return TONES[status ?? ''] ?? 'grey'
}

export function toneDot(tone: Tone): string {
  return dotClasses[tone]
}

/** "noshow" → "No show", "mobilemoney" → "Mobile money", "24h_full" → "24h full". */
export function statusLabel(status: string | null | undefined): string {
  if (!status) return '—'
  const spaced = status.replace(/_/g, ' ')
  return spaced.charAt(0).toUpperCase() + spaced.slice(1)
}
