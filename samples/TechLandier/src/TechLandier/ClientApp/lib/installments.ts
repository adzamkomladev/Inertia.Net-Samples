/** Mirrors the backend BNPL rules: 5% plan fee baked in, installments 14 days apart. */
export const PLAN_FEE = 0.05
export const PLAN_COUNTS = [3, 6, 12] as const
export const PLAN_INTERVAL_DAYS = 14

const round2 = (n: number) => Math.round(n * 100) / 100

export const planTotal = (amount: number) => round2(amount * (1 + PLAN_FEE))
export const planFee = (amount: number) => round2(amount * PLAN_FEE)
export const perInstallment = (amount: number, count: number) => round2(planTotal(amount) / count)
