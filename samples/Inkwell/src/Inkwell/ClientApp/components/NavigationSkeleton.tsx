import { router } from '@inertiajs/react'
import { useEffect, useState } from 'react'
import { Skeleton } from '@/components/ui/skeleton'
import { LoadingRows } from '@/components/posts/VirtualList'

type Kind = 'article' | 'posts' | 'users'
export type PendingVisit = { kind: Kind | null; samePage: boolean }

const GUID = '[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}'
const routesByKind: [RegExp, Kind][] = [
  [new RegExp(`^/posts/${GUID}$`, 'i'), 'article'],
  [/^\/(posts|feed|bookmarks|me\/posts|me\/archives)$/, 'posts'],
  [new RegExp(`^/users/${GUID}$`, 'i'), 'posts'],
  [/^\/users$/, 'users'],
]

/** A hover prefetch that is still loading usually lands within this, so its click shows no skeleton. */
const IN_FLIGHT_GRACE_MS = 120

/**
 * Tracks the page visit in flight: which kind of page it is heading to, and whether it only changes the query of the
 * current page (a topic, feed mode or search). Partial reloads, prefetches and form posts are ignored.
 */
export function usePendingVisit(): PendingVisit | null {
  const [pending, setPending] = useState<PendingVisit | null>(null)
  useEffect(() => {
    let timer: ReturnType<typeof setTimeout> | undefined
    const clear = () => { clearTimeout(timer); timer = undefined; setPending(null) }
    const offStart = router.on('start', event => {
      const visit = event.detail.visit
      if (visit.method !== 'get' || visit.only.length > 0 || visit.prefetch || visit.async) return
      const path = visit.url.pathname.replace(/\/$/, '') || '/'
      const kind = routesByKind.find(([pattern]) => pattern.test(path))?.[1] ?? null
      const samePage = path === (window.location.pathname.replace(/\/$/, '') || '/')
      // A page already prefetched (hover, 30 s cache) opens at once: no skeleton. One still being prefetched gets a short
      // grace period. Anything else shows the skeleton on the click itself, so the tap always answers immediately.
      const cached = router.getCached(visit.url)
      if (cached && !('inFlight' in cached && cached.inFlight)) return
      const show = () => {
        // The skeleton stands in for the top of the next page, so show it where that page will open.
        if (kind && !samePage && !visit.preserveScroll) window.scrollTo({ top: 0 })
        setPending({ kind, samePage })
      }
      clearTimeout(timer)
      if (cached) timer = setTimeout(show, IN_FLIGHT_GRACE_MS)
      else show()
    })
    const offFinish = router.on('finish', clear)
    return () => { offStart(); offFinish(); clearTimeout(timer) }
  }, [])
  return pending
}

function ArticleSkeleton() {
  return (
    <div className="mx-auto flex max-w-[44rem] flex-col gap-8 sm:gap-10">
      <div className="flex flex-col gap-5 sm:gap-6">
        <div className="space-y-3">
          <Skeleton className="h-9 w-11/12 sm:h-12" />
          <Skeleton className="h-9 w-2/3 sm:h-12" />
        </div>
        <div className="flex items-center gap-3">
          <Skeleton className="size-11 shrink-0 rounded-full" />
          <div className="space-y-2"><Skeleton className="h-3.5 w-32" /><Skeleton className="h-3 w-44" /></div>
        </div>
      </div>
      <div className="space-y-4">
        {['w-full', 'w-full', 'w-11/12', 'w-full', 'w-4/5', 'w-full', 'w-2/3'].map((width, i) => (
          <Skeleton key={i} className={`h-4 ${width}`} />
        ))}
      </div>
    </div>
  )
}

function ListSkeleton({ kind }: { kind: 'posts' | 'users' }) {
  return (
    <div className="mx-auto max-w-[44rem]">
      <div className="mb-6 space-y-3"><Skeleton className="h-9 w-48" /><Skeleton className="h-4 w-72 max-w-full" /></div>
      <LoadingRows kind={kind} />
    </div>
  )
}

/** The placeholder for the page a reader is heading to, shaped like it, so the old page never sits there looking stuck. */
export function NavigationSkeleton({ kind }: { kind: Kind }) {
  return (
    <div role="status" aria-label="Loading page" className="animate-in fade-in duration-100">
      {kind === 'article' ? <ArticleSkeleton /> : <ListSkeleton kind={kind} />}
    </div>
  )
}
