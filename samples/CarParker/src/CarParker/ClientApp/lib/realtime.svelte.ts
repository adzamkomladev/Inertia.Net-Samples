import { onMount } from 'svelte'
import { HubConnectionBuilder, LogLevel, type HubConnection } from '@microsoft/signalr'
import { router, usePage } from '@inertiajs/svelte'
import { playNotificationSound, unlockNotificationSound } from '@/lib/notification-sound'
import { toast } from 'svelte-sonner'

/**
 * SignalR wiring for the parking deck: connects to /realtime with cookie auth and
 * refreshes page props on live events. Pages opt in by which props they carry:
 *  - `spots` / `lot` / `lots` → authoritative, debounced partial reloads
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
  let removeStart: (() => void) | undefined
  let removeFinish: (() => void) | undefined

  const reloadPulse = () => {
    const props = page.props as Record<string, any>
    if (props.lot) router.reload({ only: ['lot', 'spots', 'calendar'] })
    else if (props.spots) router.reload({ only: ['spots', 'queue', 'openSessions'] })
    else if (props.lots) router.reload({ only: ['lots'] })
  }

  /** Staff boards: refresh the working set for the active console page. */
  const reloadBoard = () => {
    const component = page.component
    if (component === 'guard/Console') router.reload({ only: ['spots', 'queue', 'openSessions'] })
    else if (component === 'admin/Index') router.reload({ only: ['overview'] })
    else if (component === 'admin/Refunds') router.reload({ only: ['pending', 'decided'] })
    else if (component === 'admin/Insurance') router.reload({ only: ['policies', 'claims'] })
    else if (component === 'admin/Sessions') router.reload({ only: ['openSessions', 'exitedSessions'] })
    else if (component === 'admin/Bookings') router.reload({ only: ['bookings'] })
    else if (component === 'guard/Incidents' || component === 'admin/Incidents') router.reload({ only: ['incidents'] })
    else if (component === 'admin/Analytics') router.reload({ only: ['kpis', 'series', 'topLots', 'refundMix'] })
    else if (component === 'account/Alerts') router.reload({ only: ['alerts'] })
  }

  const finishVisit = () => {
    activeVisits = Math.max(0, activeVisits - 1)
    if (activeVisits) return
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
      // Jobs may arrive out of order; refresh database state instead of applying an old spot snapshot.
      const schedulePulse = () => {
        if (disposed || attempt !== generation) return
        clearTimeout(pulseTimer)
        pulseTimer = setTimeout(() => {
          pulseTimer = undefined
          if (activeVisits) pendingPulse = true
          else reloadPulse()
        }, 800)
      }
      next.on('SpotChanged', schedulePulse)
      next.on('LotPulse', schedulePulse)
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
        playNotificationSound()
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

  const unlock = () => { void unlockNotificationSound().catch(() => {}) }
  onMount(() => {
    document.addEventListener('pointerdown', unlock, { passive: true })
    mounted = true
    removeStart = router.on('start', () => { activeVisits++ })
    removeFinish = router.on('finish', finishVisit)
    // Let hydration and Inertia's deferred-prop load settle before live events may replace page props.
    retryTimer = setTimeout(() => void start(generation), 500)
    // Mount cleanup runs only in the browser; onDestroy also runs during SSR.
    return () => {
      document.removeEventListener('pointerdown', unlock)
      disposed = true
      mounted = false
      generation++
      clearTimeout(retryTimer)
      clearTimeout(pulseTimer)
      clearTimeout(boardTimer)
      removeStart?.()
      removeFinish?.()
      void connection?.stop()
    }
  })

  // Reconnect when identity changes (staff group membership comes from role claims).
  let tracking = false
  let lastIdentity: string | undefined
  $effect(() => {
    const currentIdentity = identity()
    if (tracking && currentIdentity === lastIdentity) return
    lastIdentity = currentIdentity
    if (!tracking) {
      tracking = true
      return
    }
    if (!mounted || disposed) return
    const attempt = ++generation
    clearTimeout(retryTimer)
    void (connection?.stop() ?? Promise.resolve()).then(() => {
      if (!disposed && attempt === generation) void start(attempt)
    })
  })

}
