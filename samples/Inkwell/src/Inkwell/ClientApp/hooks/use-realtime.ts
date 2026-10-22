import { useEffect, useRef } from 'react'
import { HubConnectionBuilder, LogLevel } from '@microsoft/signalr'
import { router, usePage } from '@inertiajs/react'
import { playNotificationSound } from '@/lib/notification-sound'
import { toast } from 'sonner'

type Counts = { id: string; sequence: number; likeCount: number; commentCount: number; viewCount: number; repostCount: number }
const announced = new Set<string>()
let lastSequence = -1

export function patchCounters<T extends Record<string, any>>(items: T[], event: Counts) {
  return items.map(item => String(item.id) === String(event.id) ? { ...item, likeCount: event.likeCount, commentCount: event.commentCount, viewCount: event.viewCount, repostCount: event.repostCount } : item)
}
export function applyCounterEvent<T extends Record<string, any>>(items: T[], event: Counts, lastSequence = -1) {
  return event.sequence <= lastSequence ? { items, sequence: lastSequence, applied: false } : { items: patchCounters(items, event), sequence: event.sequence, applied: true }
}

export function useRealtime(identity?: string) {
  const page = usePage()
  const pageRef = useRef(page)
  pageRef.current = page
  useEffect(() => {
    if (typeof window === 'undefined') return
    let disposed = false
    // Counts overlapping visits (async toggles, prefetches) so counter events wait for all of them.
    let activeVisits = 0
    let notificationTimer: ReturnType<typeof setTimeout> | undefined
    let pendingNotifications = false
    let pendingAnalytics = false
    let pendingPrompt = false
    let generation = 0
    let retryTimer: ReturnType<typeof setTimeout> | undefined
    let analyticsTimer: ReturnType<typeof setTimeout> | undefined
    let connection: ReturnType<HubConnectionBuilder['build']> | undefined
    const pendingCounters = new Map<string, Counts>()
    const pendingRemoved = new Set<string>()
    const updatePosts = (events: Counts[], removed: Set<string> = new Set()) => {
      const current = pageRef.current
      const relevant = events.filter(event => (current.props.posts as any)?.data?.some((post: any) => String(post.id) === String(event.id)) || String((current.props.post as any)?.id) === String(event.id))
      const removedVisible = [...removed].filter(id => (current.props.posts as any)?.data?.some((post: any) => String(post.id) === id))
      if (!relevant.length && !removedVisible.length) return
      router.replace({ url: current.url, component: current.component, preserveState: true, preserveScroll: true, props: (old: any) => {
        const next = { ...old }
        if (old.posts?.data) next.posts = { ...old.posts, data: relevant.reduce((items, event) => patchCounters(items, event), old.posts.data).filter((post: any) => !removedVisible.includes(String(post.id))) }
        if (old.post) next.post = relevant.reduce((post, event) => String(post.id) === String(event.id) ? { ...post, likeCount: event.likeCount, commentCount: event.commentCount, viewCount: event.viewCount, repostCount: event.repostCount } : post, old.post)
        return next
      } })
    }
    const analyticsReload = () => {
      const current = pageRef.current
      if (!['Analytics/Index', 'Posts/Analytics'].includes(current.component) || /^\/posts\/(create|[^/]+\/edit)/.test(window.location.pathname)) return
      const only = current.component === 'Analytics/Index' ? ['summary', 'series', 'topPosts'] : ['summary', 'series', 'post']
      router.reload({ only })
    }
    const promptRefresh = () => {
      const current = pageRef.current
      if (!(current.props as any).posts?.data && current.component !== 'Posts/Show' || /^\/posts\/(create|[^/]+\/edit)/.test(window.location.pathname)) return
      toast('You are back online', { action: { label: 'Refresh', onClick: () => {
      const url = new URL(window.location.href); url.searchParams.delete('cursor'); url.searchParams.delete('page')
      router.visit(url, { replace: true, preserveState: true, preserveScroll: true })
      } } })
    }
    const scheduleNotifications = () => {
      clearTimeout(notificationTimer)
      notificationTimer = setTimeout(() => {
        if (disposed) return
        if (activeVisits) { pendingNotifications = true; return }
        router.reload({ only: ['notifications', ...(pageRef.current.component === 'Notifications/Index' ? ['items'] : [])] })
      }, 200)
    }
    const removeStart = router.on('start', () => { activeVisits++ })
    const removeFinish = router.on('finish', event => {
      activeVisits = Math.max(0, activeVisits - 1)
      // Only a full (non-partial) load actually fetched the new posts the prompt announced.
      const visit = event.detail.visit
      if (visit.completed && !visit.prefetch && !visit.only.length && (pageRef.current.props.posts as any)?.data) { announced.clear(); toast.dismiss('new-posts') }
      if (activeVisits) return
      if (pendingNotifications) { pendingNotifications = false; scheduleNotifications() }
      if (pendingCounters.size || pendingRemoved.size) { updatePosts([...pendingCounters.values()], pendingRemoved); pendingCounters.clear(); pendingRemoved.clear() }
      if (pendingAnalytics) { pendingAnalytics = false; analyticsReload() }
      if (pendingPrompt) { pendingPrompt = false; promptRefresh() }
    })
    const scheduleRetry = (attempt: number) => {
      if (disposed || attempt !== generation) return
      clearTimeout(retryTimer)
      retryTimer = setTimeout(() => void start(attempt), 3000)
    }
    const start = async (attempt: number) => {
      if (disposed || attempt !== generation) return
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
          if (!activeVisits) updatePosts([event])
          else pendingCounters.set(key, event)
        })
        next.on('NewPost', (id: string) => {
          if (disposed || attempt !== generation) return
          const current = pageRef.current
          if (!(current.props.posts as any)?.data || /^\/posts\/(create|[^/]+\/edit)/.test(window.location.pathname)) return
          const key = String(id)
          if (announced.has(key)) return
          announced.add(key)
          if (announced.size > 200) announced.delete(announced.values().next().value!)
          toast(`${announced.size} new post${announced.size === 1 ? '' : 's'} available`, { id: 'new-posts', duration: Infinity, action: { label: 'Refresh', onClick: () => {
            announced.clear(); toast.dismiss('new-posts')
            if (!(pageRef.current.props as any).posts?.data || /^\/posts\/(create|[^/]+\/edit)/.test(window.location.pathname)) return
            const url = new URL(window.location.href); url.searchParams.delete('cursor'); url.searchParams.delete('page')
            router.visit(url, { replace: true, preserveState: true, preserveScroll: true })
          } } })
        })
        next.on('PostRemoved', (id: string) => { if (disposed || attempt !== generation) return; const key = String(id); if (activeVisits) pendingRemoved.add(key); else updatePosts([], new Set([key])) })
        next.on('AnalyticsChanged', (postId: string) => {
          if (disposed || attempt !== generation) return
          const current = pageRef.current
          if (current.component === 'Analytics/Index' || (current.component === 'Posts/Analytics' && String((current.props.post as any)?.id) === String(postId))) {
            analyticsTimer ??= setTimeout(() => {
              analyticsTimer = undefined
              if (activeVisits) pendingAnalytics = true
              else analyticsReload()
            }, 1000)
          }
        })
        next.on('Notification', ({ message, href }: { message: string; href: string }) => { if (!disposed && attempt === generation) { scheduleNotifications(); playNotificationSound(); toast(message, { action: { label: 'Open', onClick: () => router.visit(href) } }) } })
        next.onreconnected(() => {
          if (disposed || attempt !== generation) return
          lastSequence = -1
          scheduleNotifications()
          const current = pageRef.current
          if (current.component === 'Analytics/Index' || current.component === 'Posts/Analytics') activeVisits ? pendingAnalytics = true : analyticsReload()
          else if ((current.props as any).posts?.data || current.component === 'Posts/Show') activeVisits ? pendingPrompt = true : promptRefresh()
        })
        next.onclose(() => scheduleRetry(attempt))
        await next.start()
      } catch {
        scheduleRetry(attempt)
      }
    }
    void start(generation)
    return () => { disposed = true; generation++; clearTimeout(retryTimer); clearTimeout(analyticsTimer); clearTimeout(notificationTimer); announced.clear(); toast.dismiss('new-posts'); removeStart(); removeFinish(); void connection?.stop() }
  }, [identity])
}
