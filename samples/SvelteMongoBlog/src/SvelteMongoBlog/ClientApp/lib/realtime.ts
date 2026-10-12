/** Live counter events broadcast by the SignalR hub. Shared with `lib/realtime.svelte.ts` and its unit checks. */
export type CounterEvent = {
  id: string
  sequence: number
  likeCount: number
  commentCount: number
  viewCount: number
  repostCount: number
}

export function patchCounters<T extends Record<string, any>>(items: T[], event: CounterEvent): T[] {
  return items.map(item =>
    String(item.id) === String(event.id)
      ? { ...item, likeCount: event.likeCount, commentCount: event.commentCount, viewCount: event.viewCount, repostCount: event.repostCount }
      : item,
  )
}

export function applyCounterEvent<T extends Record<string, any>>(items: T[], event: CounterEvent, lastSequence = -1) {
  return event.sequence <= lastSequence
    ? { items, sequence: lastSequence, applied: false }
    : { items: patchCounters(items, event), sequence: event.sequence, applied: true }
}
