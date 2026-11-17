import { readonly, ref } from 'vue'
import { router } from '@inertiajs/vue3'

export type PendingKind = 'grid' | 'product' | 'checkout' | 'list' | 'dashboard'
export type PendingVisit = { kind: PendingKind; samePage: boolean }

const pending = ref<PendingVisit | null>(null)
let started = false

const GRID = /^\/(products|deals)$/
const PRODUCT = /^\/products\/[^/]+$/
const CHECKOUT = /^\/buy\/[^/]+$/

/** What a path looks like while it loads; null for pages that open without a placeholder (home, auth, policy). */
const kindOf = (path: string): PendingKind | null => {
  if (GRID.test(path)) return 'grid'
  if (PRODUCT.test(path)) return 'product'
  if (CHECKOUT.test(path)) return 'checkout'
  if (path === '/admin' || path === '/account') return 'dashboard'
  if (path.startsWith('/admin/') || path.startsWith('/account/')) return 'list'
  return null
}

/** A hover prefetch that is still loading usually lands within this, so its click shows no skeleton. */
const IN_FLIGHT_GRACE_MS = 120

function start() {
  if (started || typeof window === 'undefined') return
  started = true
  let timer: ReturnType<typeof setTimeout> | undefined
  const clear = () => {
    clearTimeout(timer)
    timer = undefined
    pending.value = null
  }
  router.on('start', (event) => {
    const visit = event.detail.visit
    if (visit.method !== 'get' || visit.only.length > 0 || visit.prefetch || visit.async) return
    const path = visit.url.pathname.replace(/\/$/, '') || '/'
    const kind = kindOf(path)
    const samePage = path === (window.location.pathname.replace(/\/$/, '') || '/')
    // Filters and pagination keep the page where it is; only a different page gets a placeholder.
    if (!kind || samePage) return
    // Already prefetched (hover, 30 s cache): opens at once. Still being prefetched: a short grace period.
    // Anything else shows the placeholder on the click itself, so the tap always answers immediately.
    const cached = router.getCached(visit.url)
    if (cached && !('inFlight' in cached && cached.inFlight)) return
    const show = () => {
      if (!visit.preserveScroll) window.scrollTo({ top: 0 })
      pending.value = { kind, samePage }
    }
    clearTimeout(timer)
    if (cached) timer = setTimeout(show, IN_FLIGHT_GRACE_MS)
    else show()
  })
  router.on('finish', clear)
}

/** The page visit in flight, shared by every layout. */
export function usePendingVisit() {
  start()
  return readonly(pending)
}
