import { onMounted, watch } from 'vue'
import { xsrf } from '@/lib/xsrf'

/**
 * Records one real visit per product after mount, so cached/prefetched navigations still count.
 * Watches the id value only: live updates replace the `product` object and must not record another view.
 * Analytics failures never interrupt browsing.
 */
export function useViewBeacon(productId: () => string) {
  onMounted(() => watch(productId, id => {
    void fetch(`/products/${id}/view`, {
      method: 'POST',
      credentials: 'same-origin',
      headers: { 'X-XSRF-TOKEN': xsrf(), 'Content-Type': 'application/json' },
      body: JSON.stringify({ referrer: document.referrer || null }),
    }).catch(() => {})
  }, { immediate: true }))
}
