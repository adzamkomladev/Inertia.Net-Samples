import { onMount } from 'svelte'
import { HubConnectionBuilder, LogLevel, type HubConnection } from '@microsoft/signalr'
import { router, usePage } from '@inertiajs/svelte'
import { playNotificationSound, unlockNotificationSound } from '@/lib/notification-sound'
import { toast } from 'svelte-sonner'

/**
 * Raw SignalR wiring for the parking deck storefront. Connects to /realtime with cookie auth and
 * refreshes page props on live events. The staff consoles, the notification badge and the alerts
 * page are Inertia live props (over /inertia-live, wired in app.ts) and refresh themselves, so this
 * connection only owns what live props cannot: pages that hold Scroll props:
 *  - `spots` / `lot` / `lots` cards → authoritative, debounced partial reloads
 *  - everyone                       → Notification toasts (the badge is a live prop)
 */
export function useRealtime(identity: () => string | undefined) {
  const page = usePage()
  let connection: HubConnection | undefined
  let retryTimer: ReturnType<typeof setTimeout> | undefined
  let pulseTimer: ReturnType<typeof setTimeout> | undefined
  let disposed = false
  let mounted = false
  let generation = 0
  // Counts overlapping visits (toggles, prefetches) so events wait for all of them.
  let activeVisits = 0
  let pendingPulse = false
  let removeStart: (() => void) | undefined
  let removeFinish: (() => void) | undefined

  const reloadPulse = () => {
    const props = page.props as Record<string, any>
    if (props.lot) router.reload({ only: ['lot', 'spots', 'calendar'] })
    else if (props.spots) router.reload({ only: ['spots', 'queue', 'openSessions'] })
    else if (props.lots) router.reload({ only: ['lots'] })
  }

  const finishVisit = () => {
    activeVisits = Math.max(0, activeVisits - 1)
    if (activeVisits) return
    if (pendingPulse) {
      pendingPulse = false
      reloadPulse()
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
      next.on('Notification', ({ message, href }: { message: string; href?: string | null }) => {
        if (disposed || attempt !== generation) return
        // The badge and the alerts page are live props and refresh themselves; this event only speaks.
        playNotificationSound()
        toast(message, href ? { action: { label: 'Open', onClick: () => router.visit(href) } } : undefined)
      })
      next.onreconnected(() => {
        if (disposed || attempt !== generation) return
        // Reconnect may have missed events: pull the current page's pulse props once.
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
      removeStart?.()
      removeFinish?.()
      void connection?.stop()
    }
  })

  // Reconnect when identity changes so the notification channel follows the signed-in user.
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
