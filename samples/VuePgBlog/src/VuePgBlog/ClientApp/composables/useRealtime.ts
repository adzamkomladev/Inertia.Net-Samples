import { onMounted, onUnmounted, watch, type Ref } from 'vue'
import { HubConnectionBuilder, LogLevel, type HubConnection } from '@microsoft/signalr'
import { router, usePage } from '@inertiajs/vue3'
import { toast } from 'vue-sonner'

type Counts = { id: string; sequence: number; likeCount: number; commentCount: number; viewCount: number; repostCount: number }
const announced = new Set<string>()
let lastSequence = -1

export function patchCounters<T extends Record<string, any>>(items: T[], event: Counts) {
  return items.map(item => String(item.id) === String(event.id) ? { ...item, likeCount: event.likeCount, commentCount: event.commentCount, viewCount: event.viewCount, repostCount: event.repostCount } : item)
}
export function applyCounterEvent<T extends Record<string, any>>(items: T[], event: Counts, lastSequence = -1) {
  return event.sequence <= lastSequence ? { items, sequence: lastSequence, applied: false } : { items: patchCounters(items, event), sequence: event.sequence, applied: true }
}

export function useRealtime(identity: Ref<string | undefined>) {
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
  const pendingCounters = new Map<string, Counts>()
  const pendingRemoved = new Set<string>()
  const updatePosts = (events: Counts[], removed: Set<string> = new Set()) => {
    const relevant = events.filter(event => (page.props.posts as any)?.data?.some((post: any) => String(post.id) === String(event.id)) || String((page.props.post as any)?.id) === String(event.id))
    const removedVisible = [...removed].filter(id => (page.props.posts as any)?.data?.some((post: any) => String(post.id) === id))
    if (!relevant.length && !removedVisible.length) return
    router.replace({ url: page.url, component: page.component, preserveState: true, preserveScroll: true, props: (old: any) => {
    const next = { ...old }
    if (old.posts?.data) next.posts = { ...old.posts, data: relevant.reduce((items, event) => patchCounters(items, event), old.posts.data).filter((post: any) => !removedVisible.includes(String(post.id))) }
    if (old.post) next.post = relevant.reduce((post, event) => String(post.id) === String(event.id) ? { ...post, likeCount: event.likeCount, commentCount: event.commentCount, viewCount: event.viewCount, repostCount: event.repostCount } : post, old.post)
    return next
    } })
  }
  const analyticsReload = () => {
    if (!['Analytics/Index', 'Analytics/Post'].includes(page.component) || /^\/posts\/(create|\d+\/edit)/.test(window.location.pathname)) return
    const only = page.component === 'Analytics/Index' ? ['kpis', 'series', 'topPosts'] : ['kpis', 'series', 'post', 'referrers']
    router.reload({ only })
  }
  const promptRefresh = () => {
    if (!(page.props.posts as any)?.data && !page.props.post || /^\/posts\/(create|\d+\/edit)/.test(window.location.pathname)) return
    toast('You are back online', { action: { label: 'Refresh', onClick: () => {
    const url = new URL(window.location.href); url.searchParams.delete('cursor'); url.searchParams.delete('page')
    router.visit(url, { replace: true, preserveState: true, preserveScroll: true })
    } } })
  }
  const finishVisit = (event: { detail: { visit: { completed: boolean; prefetch: boolean; only: string[] } } }) => {
    activeVisits = Math.max(0, activeVisits - 1)
    // Only a full (non-partial) load actually fetched the new posts the prompt announced.
    const visit = event.detail.visit
    if (visit.completed && !visit.prefetch && !visit.only.length && (page.props.posts as any)?.data) { announced.clear(); toast.dismiss('new-posts') }
    if (activeVisits) return
    if (pendingCounters.size || pendingRemoved.size) { updatePosts([...pendingCounters.values()], pendingRemoved); pendingCounters.clear(); pendingRemoved.clear() }
    if (pendingAnalytics) { pendingAnalytics = false; analyticsReload() }
    if (pendingPrompt) { pendingPrompt = false; promptRefresh() }
  }
  const scheduleRetry = (attempt: number) => {
    if (disposed || attempt !== generation) return
    clearTimeout(retryTimer)
    retryTimer = setTimeout(() => void start(attempt), 3000)
  }
  const start = async (attempt: number) => {
    if (disposed || !mounted || attempt !== generation || typeof window === 'undefined') return
    lastSequence = -1
    try {
      const next = new HubConnectionBuilder().withUrl('/realtime').withAutomaticReconnect().configureLogging(LogLevel.Warning).build()
      connection = next
      next.on('PostChanged', (event: Counts) => {
        if (disposed || attempt !== generation) return
        const key = String(event.id)
        const result = applyCounterEvent([], event, lastSequence)
        if (!result.applied) return
        lastSequence = result.sequence
        if (!activeVisits) updatePosts([event]); else pendingCounters.set(key, event)
      })
      next.on('NewPost', (id: string) => {
        if (disposed || attempt !== generation) return
        if (!(page.props.posts as any)?.data || /^\/posts\/(create|\d+\/edit)/.test(window.location.pathname)) return
        const key = String(id)
        if (announced.has(key)) return
        announced.add(key)
        if (announced.size > 200) announced.delete(announced.values().next().value!)
        toast(`${announced.size} new post${announced.size === 1 ? '' : 's'} available`, { id: 'new-posts', duration: Infinity, action: { label: 'Refresh', onClick: () => {
          announced.clear(); toast.dismiss('new-posts')
          if (!(page.props.posts as any)?.data || /^\/posts\/(create|\d+\/edit)/.test(window.location.pathname)) return
          const url = new URL(window.location.href); url.searchParams.delete('cursor'); url.searchParams.delete('page')
          router.visit(url, { replace: true, preserveState: true, preserveScroll: true })
        } } })
      })
      next.on('PostRemoved', (id: string) => { if (disposed || attempt !== generation) return; const key = String(id); if (activeVisits) pendingRemoved.add(key); else updatePosts([], new Set([key])) })
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
      next.on('Notification', ({ message, href }: { message: string; href: string }) => { if (!disposed && attempt === generation) toast(message, { action: { label: 'Open', onClick: () => router.visit(href) } }) })
      next.onreconnected(() => {
        if (disposed || attempt !== generation) return
        lastSequence = -1
        if (page.component === 'Analytics/Index' || page.component === 'Analytics/Post') activeVisits ? pendingAnalytics = true : analyticsReload()
        else if ((page.props.posts as any)?.data || (page.props.post && !/^\/posts\/(create|\d+\/edit)/.test(window.location.pathname))) activeVisits ? pendingPrompt = true : promptRefresh()
      })
      next.onclose(() => scheduleRetry(attempt))
      await next.start()
    } catch { scheduleRetry(attempt) }
  }
  onMounted(() => {
    mounted = true
    removeStart = router.on('start', () => { activeVisits++ })
    removeFinish = router.on('finish', finishVisit)
    void start(generation)
  })
  watch(identity, async () => {
    if (!mounted || disposed) return
    const attempt = ++generation
    clearTimeout(retryTimer)
    lastSequence = -1
    announced.clear(); toast.dismiss('new-posts')
    await connection?.stop()
    if (!disposed && attempt === generation) void start(attempt)
  })
  onUnmounted(() => {
    disposed = true; mounted = false; generation++
    clearTimeout(retryTimer); clearTimeout(analyticsTimer)
    announced.clear(); toast.dismiss('new-posts')
    removeStart?.(); removeFinish?.(); void connection?.stop()
  })
}
