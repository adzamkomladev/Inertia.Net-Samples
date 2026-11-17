import { router } from '@inertiajs/svelte'

export type PendingKind = 'lots' | 'lot' | 'book' | 'list' | 'dashboard' | 'guard'
export type PendingVisit = { kind: PendingKind }

const LOTS = /^\/lots$/
const LOT = /^\/lots\/[^/]+$/
const BOOK = /^\/lots\/[^/]+\/book$/

/** What a path looks like while it loads; null for pages that open without a placeholder (home, auth, policy). */
const kindOf = (path: string): PendingKind | null => {
  if (LOTS.test(path)) return 'lots'
  if (BOOK.test(path)) return 'book'
  if (LOT.test(path)) return 'lot'
  if (path === '/admin' || path === '/account') return 'dashboard'
  if (path === '/guard') return 'guard'
  if (path.startsWith('/admin/') || path.startsWith('/account/') || path.startsWith('/guard/')) return 'list'
  return null
}

/** A hover prefetch that is still loading usually lands within this, so its click shows no skeleton. */
const IN_FLIGHT_GRACE_MS = 120

const state = $state<{ current: PendingVisit | null }>({ current: null })
let started = false

function start() {
  if (started || typeof window === 'undefined') return
  started = true
  let timer: ReturnType<typeof setTimeout> | undefined
  const clear = () => {
    clearTimeout(timer)
    timer = undefined
    state.current = null
  }
  router.on('start', (event) => {
    const visit = event.detail.visit
    if (visit.method !== 'get' || visit.only.length > 0 || visit.prefetch || visit.async) return
    const path = visit.url.pathname.replace(/\/$/, '') || '/'
    const kind = kindOf(path)
    const samePage = path === (window.location.pathname.replace(/\/$/, '') || '/')
    // Filters and pagination keep the page where it is; only a different page gets a placeholder.
    if (!kind || samePage) return
    // Prefetched (hover cache): opens at once. Still prefetching: a short grace. Otherwise the placeholder shows on the click itself.
    const cached = router.getCached(visit.url)
    if (cached && !('inFlight' in cached && cached.inFlight)) return
    const show = () => {
      if (!visit.preserveScroll) window.scrollTo({ top: 0 })
      state.current = { kind }
    }
    clearTimeout(timer)
    if (cached) timer = setTimeout(show, IN_FLIGHT_GRACE_MS)
    else show()
  })
  router.on('finish', clear)
}

/** The page visit in flight, shared by every layout: read `.current`. */
export function usePendingVisit() {
  start()
  return state
}
