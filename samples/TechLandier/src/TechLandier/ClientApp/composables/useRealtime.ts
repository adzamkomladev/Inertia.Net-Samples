import { onMounted, onUnmounted, watch, type Ref } from 'vue'
import { HubConnectionBuilder, LogLevel, type HubConnection } from '@microsoft/signalr'
import { router, usePage } from '@inertiajs/vue3'
import { toast } from 'vue-sonner'

import { playNotificationSound, unlockNotificationSound } from '@/lib/notification-sound'

type Notification = { title: string; body: string; href?: string | null }

const ADMIN_PAGES = ['admin/Index', 'admin/Analytics', 'admin/Orders', 'admin/Deliveries', 'admin/Credit']

/**
 * StoreHub wiring (server → client only):
 *  - OrderChanged  (admins)  → partial-reload admin KPI strips (throttled)
 *  - StockAlert    (admins)  → warning toast + KPI reload
 *  - Notification  (user/admins) → sonner toast with an Open action
 *  - OrderTimelineUpdated (owning user) → authorized partial reload of the open
 *    tracking/order page; elsewhere a toast nudges the customer.
 * Reloads wait for in-flight visits, mirroring the sample's queue-then-flush pattern.
 */
export function useRealtime(identity: Ref<string | undefined>) {
  const page = usePage()
  let connection: HubConnection | undefined
  let retryTimer: ReturnType<typeof setTimeout> | undefined
  let kpiTimer: ReturnType<typeof setTimeout> | undefined
  let disposed = false
  let mounted = false
  let generation = 0
  let activeVisits = 0
  let pendingKpis = false
  let removeStart: (() => void) | undefined
  let removeFinish: (() => void) | undefined

  const isAdminPage = () => ADMIN_PAGES.includes(page.component) || page.component.startsWith('admin/')
  const orderNumberOnPage = (): string | null => {
    const props = page.props as Record<string, unknown>
    const order = props.order as { number?: string } | undefined
    return order?.number ?? null
  }

  const reloadKpis = () => {
    if (!isAdminPage()) return
    router.reload({ only: ['kpis', 'revenueSeries', 'lowStock', 'attention', ...(page.component === 'admin/Orders' ? ['orders'] : page.component === 'admin/Deliveries' ? ['deliveries', 'riders', 'zones'] : page.component === 'admin/Order' ? ['order', 'events', 'delivery', 'payments', 'plan'] : [])], preserveState: true, preserveScroll: true } as never)
  }
  const scheduleKpis = () => {
    if (!isAdminPage()) return
    if (activeVisits) {
      pendingKpis = true
      return
    }
    kpiTimer ??= setTimeout(() => {
      kpiTimer = undefined
      reloadKpis()
    }, 800)
  }

  const reloadTimeline = (orderNumber: string) => {
    const onTrack = page.component === 'orders/Track' && orderNumberOnPage() === orderNumber
    const onAccountOrder =
      page.component === 'account/Order' && window.location.pathname.endsWith(`/${orderNumber}`)
    if (onTrack || onAccountOrder) {
      router.reload({ only: ['order', 'events', 'delivery', 'plan'], preserveState: true, preserveScroll: true } as never)
      return true
    }
    return false
  }

  const openNotification = (n: Notification) => {
    playNotificationSound()
    toast(n.title, {
      description: n.body || undefined,
      action: n.href ? { label: 'Open', onClick: () => router.visit(n.href!) } : undefined,
    })
  }

  const finishVisit = (event: { detail: { visit: { completed: boolean; prefetch: boolean; only: string[] } } }) => {
    activeVisits = Math.max(0, activeVisits - 1)
    if (activeVisits) return
    if (pendingKpis && event.detail.visit.completed) {
      pendingKpis = false
      reloadKpis()
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
      next.on('OrderChanged', (order: { orderNumber: string; status: string }) => {
        if (disposed || attempt !== generation) return
        openNotification({ title: `Order ${order.orderNumber}`, body: `Status: ${order.status.replaceAll('_', ' ')}`, href: `/admin/orders/${order.orderNumber}` })
        scheduleKpis()
      })
      next.on('StockAlert', (alert: { productId: string; stock: number }) => {
        if (disposed || attempt !== generation) return
        playNotificationSound()
        toast.warning('Low stock alert', { description: `A product is down to ${alert.stock} units.` })
        scheduleKpis()
      })
      next.on('OrderTimelineUpdated', (event: { orderNumber: string }) => {
        if (disposed || attempt !== generation) return
        if (!reloadTimeline(event.orderNumber))
          toast.info(`Order ${event.orderNumber} was updated`, { action: { label: 'View', onClick: () => router.visit(`/orders/${event.orderNumber}`) } })
      })
      next.on('Notification', (n: Notification) => {
        if (!disposed && attempt === generation) openNotification(n)
      })
      next.onreconnected(() => {
        if (disposed || attempt !== generation) return
        if (isAdminPage()) reloadKpis()
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
    removeStart = router.on('start', () => {
      activeVisits++
    })
    removeFinish = router.on('finish', finishVisit)
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
    clearTimeout(kpiTimer)
    removeStart?.()
    removeFinish?.()
    void connection?.stop()
  })
}
