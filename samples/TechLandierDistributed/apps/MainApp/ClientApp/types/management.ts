export type BusinessHours = { day: number; closed: boolean; opens: string; closes: string; deliveryClosed: boolean; deliveryOpens: string; deliveryCloses: string }
export type StoreSettings = { name: string; email: string; phone: string; address: string; timeZoneId: string; hours: BusinessHours[] }
