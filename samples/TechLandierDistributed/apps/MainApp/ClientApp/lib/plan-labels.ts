import type { PlanBrief } from '@/types'

export interface PaymentLook {
  /** Key into the StatusBadge tone map (`kind` + `status`). */
  kind: 'payment' | 'plan'
  status: string
  label: string
}

/** True when an installment plan still has money owing on a live order. */
export const planOutstanding = (orderStatus: string, paymentStatus: string, plan?: PlanBrief | null): boolean =>
  !!plan && plan.paid < plan.count && orderStatus !== 'cancelled' && paymentStatus !== 'refunded'

/** "On plan · 2 of 6 paid": the one wording for an order that is paid in installments. */
export const planProgress = (plan: PlanBrief): string => `On plan · ${plan.paid} of ${plan.count} paid`

/**
 * The single payment label for an order, shared by customer and staff views.
 * A plan order is never a bare "Paid" while installments are outstanding, and a
 * cancelled order that never took payment shows nothing (null) instead of "Unpaid".
 */
export function paymentLook(orderStatus: string, paymentStatus: string, plan?: PlanBrief | null): PaymentLook | null {
  if (plan && orderStatus !== 'cancelled' && paymentStatus !== 'refunded') {
    return plan.paid >= plan.count
      ? { kind: 'plan', status: 'completed', label: 'Plan paid in full' }
      : { kind: 'plan', status: 'onplan', label: planProgress(plan) }
  }
  if (orderStatus === 'cancelled' && (paymentStatus === 'unpaid' || paymentStatus === 'pending')) return null
  return { kind: 'payment', status: paymentStatus, label: '' }
}
