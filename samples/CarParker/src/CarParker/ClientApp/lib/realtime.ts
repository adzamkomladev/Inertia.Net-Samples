/** Live event payloads broadcast by the SignalR hub (`ParkHub`, /realtime). Shared by `lib/realtime.svelte.ts`. */
export type SpotChangedEvent = { lotId: string; spotId: string; state: string }
export type LotPulseEvent = { lotId: string }
export type BoardChangedEvent = { kind: string } // session | refund | incident | claim | alert
export type NotificationEvent = { message: string; href?: string | null }

/** Applies a SpotChanged flip onto a `spots` prop array (SpotView or the book-page projection). */
export function patchSpots<T extends { id: string; state: string }>(spots: T[], event: SpotChangedEvent): T[] {
  return spots.map(spot =>
    String(spot.id) === String(event.spotId) ? { ...spot, state: event.state } : spot,
  )
}
