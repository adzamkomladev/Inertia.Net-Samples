import { onMounted, watch } from 'vue'
import { xsrf } from '@/lib/xsrf'

/**
 * Records one real visit per post after mount, so cached/prefetched navigations still count.
 * Watches the id value only: live counter updates replace the `post` object and must not record another view.
 * Analytics failures never interrupt reading.
 */
export function useViewBeacon(postId: () => string) {
  onMounted(() => watch(postId, id => {
    void fetch(`/posts/${id}/view`, {
      method: 'POST',
      credentials: 'same-origin',
      headers: { 'X-XSRF-TOKEN': xsrf(), 'Content-Type': 'application/json' },
      body: JSON.stringify({ referrer: document.referrer || null }),
    }).catch(() => {})
  }, { immediate: true }))
}
