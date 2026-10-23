// Shared contract between the C# endpoints (camelCase JSON) and the Vue pages.
// Shapes mirror the projections in Features/*Service.cs — keep both sides in sync.

export interface AuthUser {
  id: string
  name: string
  email: string
  verified: boolean
  avatarUrl: string | null
  theme: Theme
  role?: string | null
}

export type Theme = 'system' | 'light' | 'dark'

export interface Toast {
  type: 'success' | 'error' | 'info'
  message: string
}

// ---------------------------------------------------------------- catalog

export interface ProductCard {
  id: string
  slug: string
  name: string
  brand: string
  categorySlug: string
  /** Lucide icon name for ProductArt (Headphones, Cpu, ...). */
  icon: string
  price: number
  compareAtPrice: number | null
  discountPercent: number
  /** The price the storefront charges today. */
  displayPrice: number
  stock: number
  soldCount: number
  ratingAvg: number
  ratingCount: number
  imageUrl: string | null
}

export interface CategoryTile {
  id: string
  slug: string
  name: string
  tagline: string
  icon: string
  count: number
}

export interface SpecRow {
  label: string
  value: string
}

export interface ProductFilterInput {
  q: string
  category: string
  min: number | null
  max: number | null
  inStock: boolean | null
  sort: string | null
}

export interface ProductDetail {
  id: string
  slug: string
  name: string
  brand: string
  categoryId: string
  categorySlug: string
  categoryName: string
  icon: string
  summary: string
  description: string
  price: number
  compareAtPrice: number | null
  discountPercent: number
  displayPrice: number
  stock: number
  lowStockThreshold: number
  soldCount: number
  warrantyMonths: number
  isInsurable: boolean
  ratingAvg: number
  ratingCount: number
  imageUrl: string | null
  specs: SpecRow[]
  images: string[]
}

export interface ReviewItem {
  id: string
  authorName: string
  rating: number
  title: string | null
  body: string
  createdAt: string
  verifiedPurchase: boolean
}

/** Static page row for /support/policy: zones with fees + ETA + pay-on-delivery support. */
export interface ZoneRow {
  id: string
  name: string
  fee: number
  etaText: string
  allowPayOnDelivery: boolean
}

// ---------------------------------------------------------------- orders

export interface OrderItemRow {
  id: string
  productId: string
  name: string
  sku: string
  /** Deterministic art meta "lucideIcon|Brand". */
  imageArt: string
  unitPrice: number
  qty: number
  lineTotal: number
  slug: string | null
}

export interface OrderView {
  id: string
  number: string
  status: string
  paymentStatus: string
  paymentMethod: string
  subtotal: number
  discountTotal: number
  shippingFee: number
  total: number
  paidAt: string | null
  shippedAt: string | null
  deliveredAt: string | null
  cancelledAt: string | null
  cancelReason: string | null
  receiptNumber: string | null
  createdAt: string
  items: OrderItemRow[]
}

export interface OrderEventRow {
  id: string
  status: string
  note: string | null
  at: string
}

export interface DeliveryView {
  id: string
  status: string
  feeCollected: number | null
  cashCollected: number | null
  failedReason: string | null
  assignedAt: string | null
  pickedUpAt: string | null
  deliveredAt: string | null
  riderName: string | null
  riderPhone: string | null
  vehicle: string | null
  service: string | null
  zoneName: string | null
  zoneId: string | null
  zoneEta: string | null
}

export interface PaymentRow {
  id: string
  method: string
  providerTxRef: string | null
  amount: number
  status: string
  paidAt: string | null
}

export interface InstallmentRow {
  id: string
  number: number
  amount: number
  dueDate: string // yyyy-MM-dd
  status: string
  paidAt: string | null
}

export interface PlanView {
  id: string
  installmentCount: number
  amountPerInstallment: number
  feeTotal: number
  status: string
  startedAt: string
  completedAt: string | null
  installments: InstallmentRow[]
}

// ---------------------------------------------------------------- account

export interface AccountStats {
  orders: number
  activePlans: number
  activeWarranties: number
  openRepairs: number
}

export interface RecentOrder {
  id: string
  number: string
  status: string
  total: number
  createdAt: string
}

export interface NextInstallment {
  id: string
  amount: number
  dueDate: string
  plan: string | null
}

export interface OpenDelivery {
  id: string
  orderNumber: string
  status: string
  rider: string | null
}

export interface AlertRow {
  id: string
  kind: string
  title: string
  body: string
  href: string | null
  createdAt: string
  readAt?: string | null
}

export interface AccountOrderRow {
  id: string
  number: string
  status: string
  paymentStatus: string
  paymentMethod: string
  total: number
  createdAt: string
  itemNames: string[]
  hasReview: boolean
}

export interface WarrantyView {
  id: string
  itemName: string
  serialNumber: string | null
  startOn: string
  endOn: string
  status: string
}

export interface InsuranceView {
  id: string
  productId: string | null
  deviceName: string
  deviceSerial: string | null
  premium: number
  coverageAmount: number
  startOn: string
  endOn: string
  status: string
}

export interface ReviewCta {
  slug: string
  name: string
  eligible: boolean
}

export interface CreditState {
  planCount: number
  blocked: boolean
}

// ---------------------------------------------------------------- admin

export interface Attention {
  ordersToConfirm: number
  deliveriesToAssign: number
  overdueTickets: number
  lowStockCount: number
  warrantyClaims: number
  insuranceClaims: number
  unverifiedUsers: number
}

export interface RevenueSeriesPoint {
  date: string
  revenue: number
  orderCount: number
  bnplShare: number
}

export interface LowStockRow {
  id: string
  name: string
  stock: number
  lowStockThreshold: number
  brand: string
}

export interface AdminKpis {
  revenue: number
  orders: number
  aov: number
  planExposure: number
}

export interface AnalyticsKpis {
  revenue: number
  orders: number
  bnplShare: number
  pickups: number
  deliveries: number
  productViews: number
}

export interface SeriesRow {
  date: string
  revenue: number
  orderCount: number
  bnplShare: number
  pickupCount: number
  deliveryCount: number
}

export interface TopProductRow {
  name: string
  revenue: number
  qty: number
}

export interface CategoryShareRow {
  label: string
  revenue: number
}

export interface AdminCatalogRow {
  id: string
  name: string
  brand: string
  slug: string
  category: string
  categoryIcon: string
  price: number
  discountPercent: number
  stock: number
  lowStockThreshold: number
  soldCount: number
  status: string
  isInsurable: boolean
  warrantyMonths: number
  imageUrl: string | null
  updatedAt: string
}

export interface ProductImageRow {
  assetId: string
  url: string
}

export interface StockMovementRow {
  id: string
  delta: number
  reason: string
  note: string | null
  orderNumber: string | null
  by: string | null
  at: string
}

export interface CatalogCounts {
  all: number
  active: number
  draft: number
  archived: number
  lowStock: number
}

export interface AdminProductDetail {
  product: EditingProduct
  slug: string
  category: string
  categoryIcon: string
  soldCount: number
  ratingAvg: number
  ratingCount: number
  createdAt: string
  updatedAt: string
  archivedAt: string | null
}

export interface AdminCategoryRow {
  id: string
  name: string
  slug: string
  icon: string
  tagline: string
  isActive: boolean
}

export interface EditingProduct {
  id: string
  name: string
  brand: string
  categoryId: string
  summary: string
  description: string
  price: number
  discountPercent: number
  stock: number
  lowStockThreshold: number
  warrantyMonths: number
  isInsurable: boolean
  keywords: string | null
  status: string
  specsJson: string
  images: ProductImageRow[]
}

export interface AdminOrderRow {
  id: string
  number: string
  status: string
  paymentStatus: string
  paymentMethod: string
  total: number
  createdAt: string
  customerName: string
  customerEmail: string | null
  delivery: { status: string; riderName: string | null } | null
  unassigned: boolean
}

export interface AdminCustomer {
  name: string
  phone: string
  street: string
  notes: string | null
  email: string | null
}

export interface AdminRiderRow {
  id: string
  name: string
  phone: string
  vehicle: string | null
  service: string
}

export interface DeliveryRow {
  id: string
  orderNumber: string
  status: string
  zoneId: string | null
  zone: string | null
  riderId: string | null
  rider: string | null
  customer: string
  phone: string
  street: string
  paymentMethod: string
  total: number
  cashCollected: number | null
  failedReason: string | null
  attempts: number
  note: string | null
  receivedBy: string | null
  createdAt: string
  assignedAt: string | null
  pickedUpAt: string | null
  deliveredAt: string | null
}

export interface RiderAdminRow {
  id: string
  name: string
  phone: string
  vehicle: string | null
  service: string
  isActive: boolean
  capacity: number
  activeRuns: number
  completed: number
}

export interface ZoneAdminRow {
  id: string
  name: string
  fee: number
  etaText: string
  allowPayOnDelivery: boolean
  sortOrder: number
  isActive: boolean
}

export interface AdminPlanRow {
  id: string
  status: string
  installmentCount: number
  amountPerInstallment: number
  feeTotal: number
  startedAt: string
  completedAt: string | null
  orderNumber: string
  customer: string
  email: string | null
  overdue: number
  unpaid: number
}

export interface WarrantyClaimRow {
  id: string
  issue: string
  status: string
  decisionNote: string | null
  createdAt: string
  updatedAt: string
  item: string
  orderNumber: string
  serial: string | null
  customer: string
}

export interface InsuranceClaimRow {
  id: string
  incidentType: string
  description: string
  incidentOn: string
  payoutAmount: number | null
  status: string
  paidAt: string | null
  createdAt: string
  device: string
  coverage: number
  customer: string
}

export interface RepairRow {
  id: string
  deviceName: string
  deviceSerial: string | null
  problem: string
  status: string
  quotedCost: number | null
  technicianName: string | null
  appointmentOn: string | null
  createdAt: string
  updatedAt?: string
  customer?: string
  email?: string | null
}

export interface RepairHistoryRow {
  status: string
  note: string | null
  at: string
}

export interface TicketMessageRow {
  id: string
  isStaff: boolean
  body: string
  at: string
}

export interface AccountTicketRow {
  id: string
  number: string
  subject: string
  category: string
  status: string
  priority: string
  dueAt: string
  resolvedAt: string | null
  createdAt: string
  orderNumber: string | null
  messages: TicketMessageRow[]
}

export interface AdminTicketRow {
  id: string
  number: string
  subject: string
  category: string
  status: string
  priority: string
  dueAt: string
  createdAt: string
  guestEmail: string | null
  customerName: string | null
  customerEmail: string | null
  messageCount: number
}

export interface AdminTicketDetail {
  id: string
  number: string
  subject: string
  category: string
  status: string
  priority: string
  dueAt: string
  firstResponseAt: string | null
  resolvedAt: string | null
  createdAt: string
  guestEmail: string | null
  customerName: string | null
  customerEmail: string | null
  orderNumber: string | null
  messages: TicketMessageRow[]
}

export interface AdminReviewRow {
  id: string
  product: string
  slug: string
  author: string
  rating: number
  title: string | null
  body: string
  status: string
  createdAt: string
}

// ---------------------------------------------------------------- infra

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

export interface UserPreferences {
  theme: Theme
  emailOrderUpdates: boolean
  emailInstallmentReminders: boolean
  emailSupportReplies: boolean
  emailPromos: boolean
  timeZoneId: string
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
