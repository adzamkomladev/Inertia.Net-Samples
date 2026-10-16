import { onUnmounted, ref } from 'vue'
import { router } from '@inertiajs/vue3'
import type { GlobalEvent } from '@inertiajs/core'

/**
 * The URL of the full-page GET that is in flight, or null. Prefetch requests, partial reloads
 * (infinite scroll, toggles) and state-preserving visits are ignored, so the value only changes
 * for real page navigations. A visit served from a prefetched response never fires `start`.
 */
export function usePageVisit() {
  const target = ref<string | null>(null)
  const clear = () => {
    target.value = null
  }
  const start = (event: GlobalEvent<'start'>) => {
    const visit = event.detail.visit
    if (
      visit.method !== 'get' ||
      visit.prefetch ||
      visit.async ||
      visit.only.length ||
      visit.preserveState === true
    )
      return
    const url = `${visit.url.pathname}${visit.url.search}`
    if (url === `${window.location.pathname}${window.location.search}`) return
    target.value = url
  }
  const removeStart = router.on('start', start)
  const removeFinish = router.on('finish', clear)
  const removeCancel = router.on('cancel', clear)
  onUnmounted(() => {
    removeStart()
    removeFinish()
    removeCancel()
  })
  return target
}
