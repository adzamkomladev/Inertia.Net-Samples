/**
 * Single tone map for every status-ish string in the domain (order/payment/delivery/
 * plan/installment/claim/ticket/priority). StatusBadge turns the tone into classes;
 * pages get consistent colors for free.
 */
export type Tone = 'primary' | 'success' | 'warning' | 'destructive' | 'info' | 'lime' | 'neutral'

export interface StatusLook {
  label: string
  tone: Tone
}

const order: Record<string, StatusLook> = {
  pending: { label: 'Pending', tone: 'warning' },
  paid: { label: 'Paid', tone: 'success' },
  processing: { label: 'Processing', tone: 'info' },
  shipped: { label: 'Shipped', tone: 'info' },
  delivered: { label: 'Delivered', tone: 'success' },
  cancelled: { label: 'Cancelled', tone: 'destructive' },
}

const payment: Record<string, StatusLook> = {
  unpaid: { label: 'Unpaid', tone: 'warning' },
  pending: { label: 'Pending', tone: 'warning' },
  paid: { label: 'Paid', tone: 'success' },
  failed: { label: 'Failed', tone: 'destructive' },
  refunded: { label: 'Refunded', tone: 'info' },
}

const delivery: Record<string, StatusLook> = {
  unassigned: { label: 'Needs rider', tone: 'warning' },
  assigned: { label: 'Assigned', tone: 'info' },
  picked_up: { label: 'Picked up', tone: 'info' },
  delivered: { label: 'Delivered', tone: 'success' },
  failed: { label: 'Failed', tone: 'destructive' },
}

const plan: Record<string, StatusLook> = {
  active: { label: 'Active', tone: 'success' },
  completed: { label: 'Completed', tone: 'primary' },
  defaulted: { label: 'Defaulted', tone: 'destructive' },
  cancelled: { label: 'Cancelled', tone: 'destructive' },
}

const installment: Record<string, StatusLook> = {
  due: { label: 'Due', tone: 'warning' },
  paid: { label: 'Paid', tone: 'success' },
  overdue: { label: 'Overdue', tone: 'destructive' },
  waived: { label: 'Waived', tone: 'neutral' },
}

const claim: Record<string, StatusLook> = {
  submitted: { label: 'Submitted', tone: 'info' },
  in_review: { label: 'In review', tone: 'warning' },
  under_review: { label: 'Under review', tone: 'warning' },
  approved: { label: 'Approved', tone: 'success' },
  replacement_shipped: { label: 'Replacement shipped', tone: 'info' },
  rejected: { label: 'Rejected', tone: 'destructive' },
  resolved: { label: 'Resolved', tone: 'success' },
  paid: { label: 'Paid out', tone: 'success' },
}

const repair: Record<string, StatusLook> = {
  received: { label: 'Received', tone: 'info' },
  quoted: { label: 'Quoted', tone: 'warning' },
  repairing: { label: 'Repairing', tone: 'info' },
  ready: { label: 'Ready', tone: 'success' },
  picked_up: { label: 'Picked up', tone: 'success' },
  closed: { label: 'Closed', tone: 'neutral' },
}

const ticket: Record<string, StatusLook> = {
  received: { label: 'Received', tone: 'info' },
  open: { label: 'Open', tone: 'warning' },
  pending_customer: { label: 'Waiting on you', tone: 'warning' },
  resolved: { label: 'Resolved', tone: 'success' },
  closed: { label: 'Closed', tone: 'neutral' },
}

const priority: Record<string, StatusLook> = {
  low: { label: 'Low', tone: 'neutral' },
  normal: { label: 'Normal', tone: 'info' },
  high: { label: 'High', tone: 'warning' },
  urgent: { label: 'Urgent', tone: 'destructive' },
}

/** Registry keyed by StatusBadge `kind`. `warranty`/`insurance` claims share the claim map; `repair` has its own. */
const product: Record<string, StatusLook> = {
  draft: { label: 'Draft', tone: 'neutral' },
  active: { label: 'Active', tone: 'success' },
  archived: { label: 'Archived', tone: 'neutral' },
}

const review: Record<string, StatusLook> = {
  published: { label: 'Published', tone: 'success' },
  hidden: { label: 'Hidden', tone: 'neutral' },
}

const rider: Record<string, StatusLook> = {
  active: { label: 'Active', tone: 'success' },
  inactive: { label: 'Inactive', tone: 'neutral' },
}

const maps: Record<string, Record<string, StatusLook>> = {
  order,
  payment,
  delivery,
  plan,
  installment,
  claim,
  warranty: claim,
  insurance: claim,
  repair,
  ticket,
  priority,
  product,
  review,
  rider,
}

const humanize = (value: string) =>
  value.replace(/_/g, ' ').replace(/\b\w/g, (c) => c.toUpperCase())

/** Resolve label + tone for any status; unknown values fall back to a neutral chip. */
export function statusLook(kind: string | undefined, status: string): StatusLook {
  const map = maps[kind ?? ''] ?? maps.order!
  return map[status] ?? { label: humanize(status), tone: 'neutral' }
}

export function statusLabel(kind: string | undefined, status: string): string {
  return statusLook(kind, status).label
}
