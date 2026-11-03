import { onMounted, onUnmounted, watch, type Ref } from 'vue'
import { HubConnectionBuilder, LogLevel, type HubConnection } from '@microsoft/signalr'
import { router, usePage } from '@inertiajs/vue3'
import { toast } from 'vue-sonner'

import { playNotificationSound, unlockNotificationSound } from '@/lib/notification-sound'

type Notification = { title: string; body: string; href?: string | null }

/**
 * Raw StoreHub wiring (server → client only): it speaks the toast wording for what just changed.
 * The props themselves — the notification bell, the admin dashboards, the order timelines — are
 * Inertia live props over /inertia-live (wired in app.ts) and refresh without this connection:
 *  - OrderChanged / StockAlert / Notification → sonner toast (with a sound for personal alerts)
 *  - OrderTimelineUpdated                     → toast while the owner is on a different page
 */
export function useRealtime(identity: Ref<string | undefined>) {
  const page = usePage()
  let connection: HubConnection | undefined
  let retryTimer: ReturnType<typeof setTimeout> | undefined
  let disposed = false
  let mounted = false
  let generation = 0

  const openNotification = (n: Notification) => {
    playNotificationSound()
    toast(n.title, {
      description: n.body || undefined,
      action: n.href ? { label: 'Open', onClick: () => router.visit(n.href!) } : undefined,
    })
  }

  const orderNumberOnPage = (): string | null => {
    const props = page.props as Record<string, unknown>
    const order = props.order as { number?: string } | undefined
    return order?.number ?? null
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
      next.on('OrderChanged', (order: { orderNumber: string; status: string }) => {
        if (disposed || attempt !== generation) return
        openNotification({ title: `Order ${order.orderNumber}`, body: `Status: ${order.status.replaceAll('_', ' ')}`, href: `/admin/orders/${order.orderNumber}` })
      })
      next.on('StockAlert', (alert: { productId: string; stock: number }) => {
        if (disposed || attempt !== generation) return
        playNotificationSound()
        toast.warning('Low stock alert', { description: `A product is down to ${alert.stock} units.` })
      })
      next.on('OrderTimelineUpdated', (event: { orderNumber: string }) => {
        if (disposed || attempt !== generation) return
        // The open tracking/order page updates itself through the live transport; elsewhere speak up.
        const onOrderPage = (page.component === 'orders/Track' && orderNumberOnPage() === event.orderNumber)
          || (page.component === 'account/Order' && window.location.pathname.endsWith(`/${event.orderNumber}`))
        if (!onOrderPage)
          toast.info(`Order ${event.orderNumber} was updated`, { action: { label: 'View', onClick: () => router.visit(`/orders/${event.orderNumber}`) } })
      })
      next.on('Notification', (n: Notification) => {
        if (!disposed && attempt === generation) openNotification(n)
      })
      next.onclose(() => scheduleRetry(attempt))
      await next.start()
    } catch {
      scheduleRetry(attempt)
    }
  }

  const unlock = () => { void unlockNotificationSound().catch(() => {}) }
  onMounted(() => {
    mounted = true
    document.addEventListener('pointerdown', unlock, { passive: true })
    void start(generation)
  })
  watch(identity, async () => {
    if (!mounted || disposed) return
    const attempt = ++generation
    clearTimeout(retryTimer)
    await connection?.stop()
    if (!disposed && attempt === generation) void start(attempt)
  })
  onUnmounted(() => {
    document.removeEventListener('pointerdown', unlock)
    disposed = true
    mounted = false
    generation++
    clearTimeout(retryTimer)
    void connection?.stop()
  })
}
