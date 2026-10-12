import { xsrf } from './xsrf'

/**
 * Records one real visit per post after mount, so cached/prefetched navigations still count.
 * Fires at most once per component instance: live counter updates replace the `post` object and must not record another view.
 * Analytics failures never interrupt reading.
 */
export function useViewBeacon(postId: () => string) {
  let recorded = false
  $effect(() => {
    const id = postId()
    if (recorded || !id || typeof document === 'undefined') return
    recorded = true
    void fetch(`/posts/${id}/view`, {
      method: 'POST',
      credentials: 'same-origin',
      headers: { 'X-XSRF-TOKEN': xsrf(), 'Content-Type': 'application/json' },
      body: JSON.stringify({ referrer: document.referrer || null }),
    }).catch(() => {})
  })
}
