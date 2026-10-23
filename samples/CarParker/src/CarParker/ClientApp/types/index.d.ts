// Shared contract between the C# endpoints (camelCase JSON) and the Svelte pages.
// Enum-valued strings come from `enum.ToString().ToLowerInvariant()` on the server
// (so NoShow → "noshow", MobileMoney → "mobilemoney", InProgress → "inprogress").

export interface AuthUser {
  id: string
  name: string
  email: string
  verified: boolean
  role: 'customer' | 'guard' | 'admin' | string
  phone: string | null
  avatarUrl: string | null
  theme: Theme
}

export type Theme = 'system' | 'light' | 'dark'

export interface Toast {
  type: 'success' | 'error' | 'info'
  message: string
}

// ---------- lots ----------

export interface LotCard {
  isActive: boolean
  open24Hours: boolean
  opens: string
  closes: string
  timeZoneId: string
  id: string
  name: string
  address: string
  area: string
  levelCount: number
  ratePerHourCents: number
  dailyMaxCents: number
  contactPhone: string | null
  spots: number
  free: number
  occupancyPct: number
  spotTypes: string[]
  insuranceCovered: boolean
  coverageCents: number
  arrivalNotes: string | null
}

/** A saved number plate (account preferences); the default one is prefilled at checkout. */
export interface Vehicle {
  plate: string
  label: string | null
  isDefault: boolean
}

export interface SpotView {
  id: string
  label: string
  /** standard | compact | handicap | covered | ev */
  type: string
  level: number
  /** free | booked | occupied | blocked */
  state: string
  lastCheckpointAt: string | null
  note: string | null
}

export interface Tariff {
  ratePerHourCents: number
  dailyMaxCents: number
  insurancePercent: number
  insuranceMinCents: number
  coverageCents: number
}

export interface DayAvailability {
  date: string // yyyy-MM-dd
  free: number
  total: number
}

export interface LotOption {
  id: string
  name: string
}

// ---------- bookings ----------

export type WindowType = 'single' | 'recurring'

export interface RecurrencePattern {
  daysOfWeek: number[] // .NET day-of-week numbers, Sunday = 0
  timeFrom: string // HH:mm UTC
  timeTo: string // HH:mm UTC
  weeks: number
}

export interface OccurrenceView {
  id: string
  index: number
  startAtUtc: string
  endAtUtc: string
  /** scheduled | active | done | cancelled | noshow */
  status: string
}

export interface BookingView {
  id: string
  lotId: string
  spotId: string
  lotName: string
  spotLabel: string
  windowType: WindowType
  totalCents: number
  parkingCents: number
  insuranceCents: number
  /** confirmed | cancelled | completed */
  status: string
  createdAt: string
  cancelledAt: string | null
  startAtUtc: string | null
  endAtUtc: string | null
  pattern: RecurrencePattern | null
  occurrenceCount: number
  plateNo: string | null
  policyStatus: string | null
  occurrences: OccurrenceView[]
  note: string | null
}

// ---------- payments / refunds ----------

export interface PaymentRow {
  id: string
  reservationId: string
  amountCents: number
  /** card | mobilemoney | payatgate */
  method: string
  /** pending | paid | failed | refunded */
  status: string
  paidAt: string | null
  createdAt: string
  lotName: string | null
  spotLabel: string | null
}

export interface RefundRow {
  id: string
  reservationId: string
  /** 24h_full | 4h_half | late_none | policy_payout */
  rule: string
  amountCents: number
  status: string
  createdAt: string
  lotName?: string | null
  spotLabel?: string | null
  requestedByName?: string | null
  reason?: string | null
  decisionNote?: string | null
  paymentId?: string
  approvedByUserId?: string | null
  paidAt?: string | null
  maxCents?: number
}

// ---------- Spot Cover ----------

export interface PolicyRow {
  id: string
  reservationId?: string
  premiumCents: number
  coverageCents: number
  status: string
  startedAt: string
  createdAt: string
  lotName?: string | null
  userName?: string | null
  userEmail?: string | null
  reservedThrough?: string | null
}

export interface ClaimRow {
  id: string
  policyId: string
  /** damage | theft | brokenwindow | other */
  kind: string
  description?: string
  claimedCents: number
  payoutCents: number | null
  status: string
  createdAt: string
  paidAt?: string | null
  incidentOn?: string
  decisionNote?: string | null
  userName?: string | null
}

// ---------- alerts ----------

export interface AlertItem {
  id: string
  kind: string
  title: string
  body: string | null
  href: string | null
  at: string
  readAt: string | null
}

// ---------- guard ----------

export interface QueueRow {
  id: string
  reservationId: string
  spotLabel: string | null
  startAtUtc: string
  endAtUtc: string
  status: string
  plateNo: string | null
}

export interface SessionRow {
  id: string
  spotLabel: string | null
  plateNo: string
  enteredAtUtc: string
  exitedAtUtc?: string | null
  cancelReason: string | null
  /** present on admin/guard board rows */
  status?: string
  lotName?: string | null
}

export interface GraceWindow {
  checkIn: number
  overstay: number
  noShow: number
}

// ---------- admin ----------

export interface AdminOverview {
  needsAttention: {
    refundsPending: number
    claimsPending: number
    overdueSessions: number
    openIncidents: number
    noShowsToday: number
  }
  today: {
    reservations: number
    sessions: number
    revenueCents: number
  }
  lots: LotCard[]
}

export interface AdminUser {
  id: string
  name: string
  email: string
  emailVerifiedAt: string | null
  createdAt: string
  role: string
}

export interface KpiSet {
  revenueCents: number
  sessions: number
  occupancyAvgPct: number
  noShowPct: number
  reservations: number
  refundsPending: number
  refundsPaidCents: number
  policyMixPct: number
}

export interface AnalyticsSeriesPoint {
  date: string // yyyy-MM-dd
  revenueCents: number
  sessions: number
  occupancyPct: number
  reservations: number
}

export interface TopLotRow {
  id: string
  name: string
  revenueCents: number
  sessions: number
  occupancyPct: number
}

export interface RefundMixRow {
  status: string
  count: number
}

// ---------- account overview ----------

export interface AccountOverview {
  upcomingCount: number
  nextVisit: {
    id: string
    startAtUtc: string
    endAtUtc: string
    spotLabel: string | null
    lotName: string | null
    bookingId: string
  } | null
  upcoming: {
    id: string
    totalCents: number
    window: WindowType
    startAtUtc: string | null
    endAtUtc: string | null
    occurrenceCount: number
    lotName: string | null
    spotLabel: string | null
  }[]
  activeSession: {
    id: string
    enteredAtUtc: string
    lotName: string | null
    spotLabel: string | null
  } | null
  spend30dCents: number
  unreadAlerts: number
}

export interface UserPreferences {
  theme: Theme
  bookingReminders: boolean
  emailOnCheckout: boolean
  emailOnRefund: boolean
}

/** Shape of an Inertia `Scroll(...)` prop (wrapper "data"). */
export interface ScrollPage<T> {
  data: T[]
}

export interface UploadedAsset {
  id: string
  url: string
  contentType: string
  size: number
}

export interface PresignResponse {
  assetId: string
  url: string
  headers: Record<string, string>
}

declare module '@inertiajs/core' {
  export interface InertiaConfig {
    sharedPageProps: {
      appName: string
      auth: { user: AuthUser | null }
    }
    flashDataType: {
      toast?: Toast
    }
    errorValueType: string
  }
}
