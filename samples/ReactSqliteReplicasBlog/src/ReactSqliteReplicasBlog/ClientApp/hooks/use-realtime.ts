import { useEffect, useRef } from 'react'
import { HubConnectionBuilder, LogLevel } from '@microsoft/signalr'
import { router, usePage } from '@inertiajs/react'
import { toast } from 'sonner'

import { refreshProps, type CounterEvent as Counts } from '../lib/realtime'

const announced = new Set<string>()

export function useRealtime(identity?: string) {
  const page = usePage()
  const pageRef = useRef(page)
  pageRef.current = page
  useEffect(() => {
    if (typeof window === 'undefined') return
    let disposed = false
    // Counts overlapping visits (async toggles, prefetches) so counter events wait for all of them.
    let activeVisits = 0
    let pendingAnalytics = false
    let pendingPrompt = false
    let generation = 0
    let retryTimer: ReturnType<typeof setTimeout> | undefined
    let analyticsTimer: ReturnType<typeof setTimeout> | undefined
    let connection: ReturnType<HubConnectionBuilder['build']> | undefined
    const pendingCounters = new Map<string, Counts>()
    const pendingRemoved = new Set<string>()
    let refreshTimer: ReturnType<typeof setTimeout> | undefined
    const updatePosts = (events: Counts[], removed: Set<string> = new Set()) => {
      const current = pageRef.current
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
        const only = refreshProps(pageRef.current.props, changed)
        if (only.length) router.reload({ only })
      }, 100)
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
    const removeStart = router.on('start', () => { activeVisits++ })
    const removeFinish = router.on('finish', event => {
      activeVisits = Math.max(0, activeVisits - 1)
      // Only a full (non-partial) load actually fetched the new posts the prompt announced.
      const visit = event.detail.visit
      if (visit.completed && !visit.prefetch && !visit.only.length && (pageRef.current.props.posts as any)?.data) { announced.clear(); toast.dismiss('new-posts') }
      if (activeVisits) return
      if (pendingCounters.size || pendingRemoved.size) { updatePosts([...pendingCounters.values()], pendingRemoved) }
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
      try {
        const next = new HubConnectionBuilder().withUrl('/realtime').withAutomaticReconnect().configureLogging(LogLevel.Warning).build()
        connection = next
        next.on('PostChanged', (event: Counts) => {
          if (disposed || attempt !== generation) return
          const key = String(event.id)
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
        next.on('Notification', ({ message, href }: { message: string; href: string }) => { if (!disposed && attempt === generation) toast(message, { action: { label: 'Open', onClick: () => router.visit(href) } }) })
        next.onreconnected(() => {
          if (disposed || attempt !== generation) return
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
    return () => { disposed = true; generation++; clearTimeout(retryTimer); clearTimeout(analyticsTimer)
    clearTimeout(refreshTimer); announced.clear(); toast.dismiss('new-posts'); removeStart(); removeFinish(); void connection?.stop() }
  }, [identity])
}
