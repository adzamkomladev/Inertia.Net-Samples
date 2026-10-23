import { InfiniteScroll } from '@inertiajs/react'
import type { ReloadOptions } from '@inertiajs/core'
import { useCallback, useRef, useState, type ReactNode } from 'react'
import { useWindowVirtualizer } from '@tanstack/react-virtual'
import { Skeleton } from '@/components/ui/skeleton'

/** Placeholder rows shaped like the real ones, shown while the next page loads. */
export function LoadingRows({ kind }: { kind: string }) {
  return (
    <div role="status" aria-label="Loading more" aria-live="polite" className="animate-in fade-in duration-300">
      {[0, 1, 2].map(i => kind === 'users' ? (
        <div key={i} className="flex items-center gap-3 border-b py-4" style={{ opacity: 1 - i * 0.4 }}>
          <Skeleton className="size-11 shrink-0 rounded-full" />
          <div className="flex-1 space-y-2"><Skeleton className="h-3.5 w-36" /><Skeleton className="h-3 w-24" /></div>
          <Skeleton className="h-8 w-24 rounded-full" />
        </div>
      ) : (
        <div key={i} className="flex min-h-[190px] gap-4 border-b py-7" style={{ opacity: 1 - i * 0.22 }}>
          <Skeleton className="size-10 shrink-0 rounded-full" />
          <div className="min-w-0 flex-1 space-y-3">
            <Skeleton className="h-3.5 w-40" />
            <Skeleton className="h-5 w-4/5" />
            <div className="space-y-2"><Skeleton className="h-3.5 w-full" /><Skeleton className="h-3.5 w-2/3" /></div>
            <div className="flex gap-2 pt-1"><Skeleton className="h-3 w-16" /><Skeleton className="h-3 w-16" /><Skeleton className="h-3 w-16" /></div>
          </div>
        </div>
      ))}
    </div>
  )
}

export default function VirtualList<T extends { id: string }>({ items, data, renderItem, label, params }: {
  items: T[]; data: string; renderItem: (item: T) => ReactNode; label: string; params?: ReloadOptions
}) {
  const list = useRef<HTMLDivElement | null>(null)
  const end = useRef<HTMLDivElement>(null)
  const [mounted, setMounted] = useState(false)
  const [margin, setMargin] = useState(0)
  // Rows from later pages ease in (staggered) the first time they appear; rows present on first render never animate.
  const [count, setCount] = useState(items.length)
  const [fresh, setFresh] = useState<Map<string, number>>(() => new Map())
  if (items.length !== count) {
    if (items.length > count) {
      const next = new Map(fresh)
      items.slice(count).forEach((item, i) => next.set(String(item.id), Math.min(i, 6) * 60))
      setFresh(next)
    }
    setCount(items.length)
  }
  const done = useRef(new Set<string>())
  const virtual = useWindowVirtualizer({ count: items.length, estimateSize: () => data === 'users' ? 81 : 240, overscan: 5, scrollMargin: margin, getItemKey: i => String(items[i]!.id) })
  // Callback ref measures the list top on mount, which avoids a mount effect.
  const measureMount = useCallback((node: HTMLDivElement | null) => {
    list.current = node
    if (!node) return
    setMargin(node.getBoundingClientRect().top + window.scrollY)
    setMounted(true)
  }, [])
  const row = (item: T) => {
    const key = String(item.id)
    const delay = done.current.has(key) ? undefined : fresh.get(key)
    return (
      <div className={delay === undefined ? undefined : 'animate-rise'} style={delay ? { animationDelay: `${delay}ms` } : undefined} onAnimationEnd={e => { if (e.target === e.currentTarget) done.current.add(key) }}>
        {renderItem(item)}
      </div>
    )
  }
  return (
    <InfiniteScroll params={params} data={data} onlyNext preserveUrl buffer={600} endElement={end} loading={<LoadingRows kind={data} />}>
      <div ref={measureMount} role="list" aria-label={label} style={mounted ? { height: virtual.getTotalSize(), position: 'relative' } : undefined}>
        {mounted ? virtual.getVirtualItems().map(r => <div key={r.key} ref={virtual.measureElement} data-index={r.index} role="listitem"
          className="absolute top-0 left-0 w-full" style={{ transform: `translateY(${r.start - margin}px)` }}>{row(items[r.index]!)}</div>)
          : items.map(item => <div key={item.id} role="listitem">{row(item)}</div>)}
      </div>
      <div ref={end} className="h-px" aria-hidden />
    </InfiniteScroll>
  )
}
