import { onDestroy, onMount } from 'svelte'
import { HubConnectionBuilder, LogLevel, type HubConnection } from '@microsoft/signalr'
import { router, usePage } from '@inertiajs/svelte'
import { toast } from 'svelte-sonner'
import type { SpotChangedEvent } from './realtime'
import { patchSpots } from './realtime'

/**
 * SignalR wiring for the parking deck: connects to /realtime with cookie auth and
 * refreshes page props on live events. Pages opt in by which props they carry:
 *  - `spots` + matching lot id → optimistic spot-state flips (no round trip)
 *  - `lot` / `lots` cards      → debounced partial reload on LotPulse
 *  - staff consoles            → debounced board reload on BoardChanged (staff group)
 *  - everyone                  → Notification toasts
 */
export function useRealtime(identity: () => string | undefined) {
  const page = usePage()
  let connection: HubConnection | undefined
  let retryTimer: ReturnType<typeof setTimeout> | undefined
  let pulseTimer: ReturnType<typeof setTimeout> | undefined
  let boardTimer: ReturnType<typeof setTimeout> | undefined
  let disposed = false
  let mounted = false
  let generation = 0
  // Counts overlapping visits (toggles, prefetches) so events wait for all of them.
  let activeVisits = 0
  let pendingPulse = false
  let pendingBoard = false
  let pendingSpots: SpotChangedEvent[] = []
  let removeStart: (() => void) | undefined
  let removeFinish: (() => void) | undefined

  const pageLotId = () => {
    const props = page.props as Record<string, any>
    if (props.lotId) return String(props.lotId)
    if (props.lot?.id) return String(props.lot.id)
    return null
  }
  const pageSpots = () => (page.props as Record<string, any>).spots as ({ id: string; state: string }[] | undefined) ?? undefined

  /** Optimistic spot flip: patch the `spots` prop in place, no reload. */
  const flipSpots = (events: SpotChangedEvent[]) => {
    if (!pageSpots()) return
    const lotId = pageLotId()
    const relevant = events.filter(event => !lotId || String(event.lotId) === lotId)
    if (!relevant.length) return
    router.replace({
      url: page.url,
      component: page.component,
      preserveState: true,
      preserveScroll: true,
      props: (old: Record<string, any>) => {
        const next = { ...old }
        if (old.spots) next.spots = relevant.reduce((spots, event) => patchSpots(spots, event), old.spots)
        return next
      },
    })
  }

  /** Debounced partial reload for card/lot data the pulse invalidated. */
  const reloadPulse = () => {
    const props = page.props as Record<string, any>
    if (props.lots) router.reload({ only: ['lots'], preserveScroll: true })
    else if (props.lot) router.reload({ only: ['lot', 'spots', 'calendar'], preserveScroll: true })
  }

  /** Staff boards: refresh the working set for the active console page. */
  const reloadBoard = () => {
    const component = page.component
    if (component === 'guard/Console') router.reload({ only: ['spots', 'queue', 'openSessions'], preserveScroll: true })
    else if (component === 'admin/Index') router.reload({ only: ['overview'], preserveScroll: true })
    else if (component === 'admin/Refunds') router.reload({ only: ['pending', 'decided'], preserveScroll: true })
    else if (component === 'admin/Insurance') router.reload({ only: ['policies', 'claims'], preserveScroll: true })
    else if (component === 'admin/Sessions') router.reload({ only: ['openSessions', 'exitedSessions'], preserveScroll: true })
    else if (component === 'admin/Bookings') router.reload({ only: ['bookings'], preserveScroll: true })
    else if (component === 'guard/Incidents' || component === 'admin/Incidents') router.reload({ only: ['incidents'], preserveScroll: true })
    else if (component === 'admin/Analytics') router.reload({ only: ['kpis', 'series', 'topLots', 'refundMix'], preserveScroll: true })
    else if (component === 'account/Alerts') router.reload({ only: ['alerts'], preserveScroll: true })
  }

  const finishVisit = (event: { detail: { visit: { completed: boolean; prefetch: boolean; only: string[] } } }) => {
    activeVisits = Math.max(0, activeVisits - 1)
    const visit = event.detail.visit
    // A full load fetched fresh data; drop queued flips so we don't overwrite newer server state.
    if (visit.completed && !visit.prefetch && !visit.only.length) pendingSpots = []
    if (activeVisits) return
    if (pendingSpots.length) {
      flipSpots(pendingSpots)
      pendingSpots = []
    }
    if (pendingPulse) {
      pendingPulse = false
      reloadPulse()
    }
    if (pendingBoard) {
      pendingBoard = false
      reloadBoard()
    }
  }

  const scheduleRetry = (attempt: number) => {
    if (disposed || attempt !== generation) return
    clearTimeout(retryTimer)
    retryTimer = setTimeout(() => void start(attempt), 3000)
  }

  const start = async (attempt: number) => {
    if (disposed || !mounted || attempt !== generation || typeof window === 'undefined') return
    try {
      const next = new HubConnectionBuilder().withUrl('/realtime').withAutomaticReconnect().configureLogging(LogLevel.Warning).build()
      connection = next
      next.on('SpotChanged', (event: SpotChangedEvent) => {
        if (disposed || attempt !== generation) return
        if (!pageSpots()) return
        if (activeVisits) pendingSpots.push(event)
        else flipSpots([event])
      })
      next.on('LotPulse', () => {
        if (disposed || attempt !== generation) return
        if (!(page.props as Record<string, any>).lots && !(page.props as Record<string, any>).lot) return
        clearTimeout(pulseTimer)
        pulseTimer = setTimeout(() => {
          pulseTimer = undefined
          if (activeVisits) pendingPulse = true
          else reloadPulse()
        }, 800)
      })
      next.on('BoardChanged', () => {
        if (disposed || attempt !== generation) return
        clearTimeout(boardTimer)
        boardTimer = setTimeout(() => {
          boardTimer = undefined
          if (activeVisits) pendingBoard = true
          else reloadBoard()
        }, 600)
      })
      next.on('Notification', ({ message, href }: { message: string; href?: string | null }) => {
        if (disposed || attempt !== generation) return
        toast(message, href ? { action: { label: 'Open', onClick: () => router.visit(href) } } : undefined)
      })
      next.onreconnected(() => {
        if (disposed || attempt !== generation) return
        // Reconnect may have missed events: pull the current page's live props once.
        if (activeVisits) pendingBoard = true
        else reloadBoard()
        if ((page.props as Record<string, any>).lots || (page.props as Record<string, any>).lot) {
          if (activeVisits) pendingPulse = true
          else reloadPulse()
        }
      })
      next.onclose(() => scheduleRetry(attempt))
      await next.start()
    } catch { scheduleRetry(attempt) }
  }

  onMount(() => {
    mounted = true
    removeStart = router.on('start', () => { activeVisits++ })
    removeFinish = finishVisit
    // Let hydration and Inertia's deferred-prop load settle before live events may replace page props.
    retryTimer = setTimeout(() => void start(generation), 500)
  })

  // Reconnect when identity changes (staff group membership comes from role claims).
  let tracking = false
  $effect(() => {
    identity()
    if (!tracking) {
      tracking = true
      return
    }
    if (!mounted || disposed) return
    const attempt = ++generation
    clearTimeout(retryTimer)
    void connection?.stop().then(() => {
      if (!disposed && attempt === generation) void start(attempt)
    })
  })

  onDestroy(() => {
    disposed = true
    mounted = false
    generation++
    clearTimeout(retryTimer)
    clearTimeout(pulseTimer)
    clearTimeout(boardTimer)
    removeStart?.()
    removeFinish?.()
    void connection?.stop()
  })
}
