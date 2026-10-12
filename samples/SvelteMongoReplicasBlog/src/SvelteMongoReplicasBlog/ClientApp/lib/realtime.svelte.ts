import { onDestroy, onMount } from 'svelte'
import { HubConnectionBuilder, LogLevel, type HubConnection } from '@microsoft/signalr'
import { router, usePage } from '@inertiajs/svelte'
import { toast } from 'svelte-sonner'
import { refreshProps, type CounterEvent } from './realtime'

const announced = new Set<string>()

export function useRealtime(identity: () => string | undefined) {
  const page = usePage()
  let connection: HubConnection | undefined
  let retryTimer: ReturnType<typeof setTimeout> | undefined
  let analyticsTimer: ReturnType<typeof setTimeout> | undefined
  let disposed = false
  let mounted = false
  let generation = 0
  // Counts overlapping visits (async toggles, prefetches) so counter events wait for all of them.
  let activeVisits = 0
  let pendingAnalytics = false
  let pendingPrompt = false
  let removeStart: (() => void) | undefined
  let removeFinish: (() => void) | undefined
  const pendingCounters = new Map<string, CounterEvent>()
  const pendingRemoved = new Set<string>()

  let refreshTimer: ReturnType<typeof setTimeout> | undefined
  const updatePosts = (events: CounterEvent[], removed: Set<string> = new Set()) => {
    const current = page
    // Refetch rather than apply unordered snapshots from multiple independent publishers.
    const visible = (id: string) => (current.props.posts as any)?.data?.some((p: any) => String(p.id) === id) || String((current.props.post as any)?.id) === id
    if (!events.some(e => visible(String(e.id))) && ![...removed].some(visible)) return
    for (const event of events) pendingCounters.set(String(event.id), event)
    for (const id of removed) pendingRemoved.add(id)
    refreshTimer ??= setTimeout(() => {
      refreshTimer = undefined
      if (disposed || activeVisits) return
      const changed = [...pendingCounters.keys(), ...pendingRemoved]
      pendingCounters.clear(); pendingRemoved.clear()
      // Editor state is never refreshed by background events.
      if (/^\/posts\/(create|[^/]+\/edit)/.test(window.location.pathname)) return
      const only = refreshProps(page.props, changed)
      if (only.length) router.reload({ only })
    }, 100)
  }

  const analyticsReload = () => {
    if (!['Analytics/Index', 'Analytics/Post'].includes(page.component) || /^\/posts\/(create|\d+\/edit)/.test(window.location.pathname)) return
    const only = page.component === 'Analytics/Index' ? ['kpis', 'series', 'topPosts'] : ['kpis', 'series', 'post', 'referrers']
    router.reload({ only })
  }

  const promptRefresh = () => {
    if (!(page.props.posts as any)?.data && !page.props.post || /^\/posts\/(create|\d+\/edit)/.test(window.location.pathname)) return
    toast('You are back online', {
      action: {
        label: 'Refresh',
        onClick: () => {
          const url = new URL(window.location.href)
          url.searchParams.delete('cursor')
          url.searchParams.delete('page')
          router.visit(url, { replace: true, preserveState: true, preserveScroll: true })
        },
      },
    })
  }

  const finishVisit = (event: { detail: { visit: { completed: boolean; prefetch: boolean; only: string[] } } }) => {
    activeVisits = Math.max(0, activeVisits - 1)
    // Only a full (non-partial) load actually fetched the new posts the prompt announced.
    const visit = event.detail.visit
    if (visit.completed && !visit.prefetch && !visit.only.length && (page.props.posts as any)?.data) {
      announced.clear()
      toast.dismiss('new-posts')
    }
    if (activeVisits) return
    if (pendingCounters.size || pendingRemoved.size) {
      updatePosts([...pendingCounters.values()], pendingRemoved)
    }
    if (pendingAnalytics) {
      pendingAnalytics = false
      analyticsReload()
    }
    if (pendingPrompt) {
      pendingPrompt = false
      promptRefresh()
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
      next.on('PostChanged', (event: CounterEvent) => {
        if (disposed || attempt !== generation) return
        const key = String(event.id)
        if (!activeVisits) updatePosts([event])
        else pendingCounters.set(key, event)
      })
      next.on('NewPost', (id: string) => {
        if (disposed || attempt !== generation) return
        if (!(page.props.posts as any)?.data || /^\/posts\/(create|\d+\/edit)/.test(window.location.pathname)) return
        const key = String(id)
        if (announced.has(key)) return
        announced.add(key)
        if (announced.size > 200) announced.delete(announced.values().next().value!)
        toast(`${announced.size} new post${announced.size === 1 ? '' : 's'} available`, {
          id: 'new-posts',
          duration: Infinity,
          action: {
            label: 'Refresh',
            onClick: () => {
              announced.clear()
              toast.dismiss('new-posts')
              if (!(page.props.posts as any)?.data || /^\/posts\/(create|\d+\/edit)/.test(window.location.pathname)) return
              const url = new URL(window.location.href)
              url.searchParams.delete('cursor')
              url.searchParams.delete('page')
              router.visit(url, { replace: true, preserveState: true, preserveScroll: true })
            },
          },
        })
      })
      next.on('PostRemoved', (id: string) => {
        if (disposed || attempt !== generation) return
        const key = String(id)
        if (activeVisits) pendingRemoved.add(key)
        else updatePosts([], new Set([key]))
      })
      next.on('AnalyticsChanged', (postId: string) => {
        if (disposed || attempt !== generation) return
        if (page.component === 'Analytics/Index' || (page.component === 'Analytics/Post' && String((page.props.post as any)?.id) === String(postId))) {
          analyticsTimer ??= setTimeout(() => {
            analyticsTimer = undefined
            if (activeVisits) pendingAnalytics = true
            else analyticsReload()
          }, 1000)
        }
      })
      next.on('Notification', ({ message, href }: { message: string; href: string }) => {
        if (!disposed && attempt === generation) toast(message, { action: { label: 'Open', onClick: () => router.visit(href) } })
      })
      next.onreconnected(() => {
        if (disposed || attempt !== generation) return
        if (page.component === 'Analytics/Index' || page.component === 'Analytics/Post') {
          if (activeVisits) pendingAnalytics = true
          else analyticsReload()
        } else if ((page.props.posts as any)?.data || (page.props.post && !/^\/posts\/(create|\d+\/edit)/.test(window.location.pathname))) {
          if (activeVisits) pendingPrompt = true
          else promptRefresh()
        }
      })
      next.onclose(() => scheduleRetry(attempt))
      await next.start()
    } catch { scheduleRetry(attempt) }
  }

  onMount(() => {
    mounted = true
    removeStart = router.on('start', () => { activeVisits++ })
    removeFinish = router.on('finish', finishVisit)
    // Let hydration and Inertia's deferred-prop load settle before live events may replace page props.
    retryTimer = setTimeout(() => void start(generation), 500)
  })

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
    announced.clear()
    toast.dismiss('new-posts')
    void connection?.stop().then(() => {
      if (!disposed && attempt === generation) void start(attempt)
    })
  })

  onDestroy(() => {
    disposed = true
    mounted = false
    generation++
    clearTimeout(retryTimer)
    clearTimeout(analyticsTimer)
  clearTimeout(refreshTimer)
    announced.clear()
    toast.dismiss('new-posts')
    removeStart?.()
    removeFinish?.()
    void connection?.stop()
  })
}
