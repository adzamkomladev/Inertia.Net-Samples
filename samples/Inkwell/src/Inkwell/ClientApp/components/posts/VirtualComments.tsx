import { Link, router, usePage } from '@inertiajs/react'
import { patchEntity, refused } from '@/hooks/use-social-mutation'
import { useVirtualizer } from '@tanstack/react-virtual'
import { useRef, useState, useCallback } from 'react'
import { Heart, Loader2, MessageCircle, Trash2 } from 'lucide-react'
import { EmptyState } from '@/components/EmptyState'
import { cn } from 'cn'
import { Button } from '@/components/ui/button'
import { Avatar, AvatarFallback, AvatarImage } from '@/components/ui/avatar'
import { initials } from '@/layouts/AppLayout'
import type { PostComment } from '@/types/posts'
import type { SharedProps } from '@/types/shared'
import { timeAgo } from './time'

function CommentRow({ comment, archived, pending, onLike, onRemove }: {
  comment: PostComment
  archived?: boolean
  pending: boolean
  onLike: (comment: PostComment) => void
  onRemove: (id: string) => void
}) {
  const user = usePage<SharedProps>().props.auth.user
  // Only animate after a click, never when a liked comment first renders.
  const [bumped, setBumped] = useState(false)
  return (
    <>
      <Avatar className="size-9">
        {comment.author.avatarUrl && <AvatarImage src={comment.author.avatarUrl} alt="" />}
        <AvatarFallback className="text-xs">{initials(comment.author.name)}</AvatarFallback>
      </Avatar>
      <div className="min-w-0 flex-1">
        <div className="flex items-center gap-1.5 text-sm">
          <Link href={`/users/${comment.author.id}`} className="truncate font-medium hover:underline">{comment.author.name}</Link>
          <span aria-hidden className="text-muted-foreground/60">·</span>
          <time dateTime={comment.createdAt} className="shrink-0 text-muted-foreground">{timeAgo(comment.createdAt)}</time>
          {comment.canDelete && !archived && (
            <Button
              variant="ghost" size="icon-xs"
              className="ml-auto rounded-full text-muted-foreground hover:bg-destructive/10 hover:text-destructive focus-visible:opacity-100 [@media(hover:hover)]:opacity-0 [@media(hover:hover)]:group-hover:opacity-100"
              aria-label={`Delete comment by ${comment.author.name}`} onClick={() => onRemove(comment.id)}
            >
              <Trash2 />
            </Button>
          )}
        </div>
        <p className="mt-0.5 text-[15px] leading-relaxed break-words whitespace-pre-wrap">{comment.body}</p>
        <button
          type="button" disabled={archived || !user?.verified} aria-busy={pending} aria-pressed={!!comment.liked}
          onClick={() => { setBumped(true); onLike(comment) }}
          className={cn(
            'group/like -ml-1.5 mt-1 flex items-center gap-1 rounded-full pr-2 text-xs text-muted-foreground transition-colors hover:text-like disabled:pointer-events-none disabled:opacity-50',
            comment.liked && 'text-like',
          )}
        >
          <span className="flex size-6 items-center justify-center rounded-full transition-colors group-hover/like:bg-like/10">
            <Heart
              className={cn('size-3.5', comment.liked && 'fill-current', bumped && comment.liked && 'animate-pop')}
              onAnimationEnd={() => setBumped(false)} aria-hidden
            />
          </span>
          <span className="tabular-nums">{comment.likeCount || ''}</span>
          <span className="sr-only">{comment.liked ? 'Unlike comment' : 'Like comment'}</span>
        </button>
      </div>
    </>
  )
}

export default function VirtualComments({ postId, comments, archived }: { postId: string; comments: PostComment[]; archived?: boolean }) {
  const scroll = useRef<HTMLDivElement>(null)
  const [mounted, setMounted] = useState(false)
  const [visible, setVisible] = useState(comments)
  const [next, setNext] = useState(comments.length === 50 ? comments[49]!.cursor ?? null : null)
  const [loading, setLoading] = useState(false)
  const [error, setError] = useState('')
  const [pending, setPending] = useState<Set<string>>(new Set())

  // Merge fresh server comments while keeping locally loaded older pages.
  const [seenComments, setSeenComments] = useState(comments)
  if (seenComments !== comments) {
    setSeenComments(comments)
    setVisible(previous => {
      const byId = new Map(previous.map(c => [c.id, c]))
      comments.forEach(c => byId.set(c.id, c))
      return [...byId.values()].sort((a, b) => b.createdAt.localeCompare(a.createdAt) || String(b.id).localeCompare(String(a.id), undefined, { numeric: true }))
    })
    if (comments.length === 50) setNext(previous => previous ?? comments[49]!.cursor ?? null)
  }

  const virtual = useVirtualizer({ count: visible.length, getScrollElement: () => scroll.current, estimateSize: () => 120, overscan: 5, getItemKey: i => String(visible[i]!.id) })
  const measureMount = useCallback((node: HTMLDivElement | null) => { if (node) setMounted(true) }, [])

  async function more() {
    if (!next || loading) return
    setLoading(true); setError('')
    try {
      const response = await fetch(`/posts/${postId}/comments?cursor=${encodeURIComponent(next)}`, { credentials: 'same-origin', headers: { Accept: 'application/json' } })
      if (!response.ok) throw new Error()
      const result: { data: PostComment[]; nextCursor: string | null } = await response.json()
      setVisible(previous => { const ids = new Set(previous.map(c => c.id)); return [...previous, ...result.data.filter(c => !ids.has(c.id))] })
      setNext(result.nextCursor)
    } catch { setError('Unable to load older comments. Please try again.') }
    finally { setLoading(false) }
  }
  // Keep the `comments` prop (and so the history snapshot) in sync with what the server confirmed.
  const patchComments = (update: (items: PostComment[]) => PostComment[]) =>
    router.replaceProp('comments', (items: unknown) => Array.isArray(items) ? update(items as PostComment[]) : items)

  function like(comment: PostComment) {
    if (pending.has(comment.id)) return
    setPending(previous => new Set(previous).add(comment.id))
    const next = { ...comment, liked: !comment.liked, likeCount: Math.max(0, (comment.likeCount ?? 0) + (comment.liked ? -1 : 1)) }
    let confirmed = false
    setVisible(items => items.map(c => c.id === comment.id ? next : c))
    const options = {
      only: ['auth'], async: true, preserveScroll: true, preserveState: true,
      onSuccess: (page: { flash?: unknown }) => { if (refused(page)) return; confirmed = true; patchComments(items => items.map(c => c.id === comment.id ? next : c)) },
      onFinish: () => {
        setPending(items => { const copy = new Set(items); copy.delete(comment.id); return copy })
        if (!confirmed) setVisible(items => items.map(c => c.id === comment.id ? comment : c))
      },
    }
    if (comment.liked) router.delete(`/comments/${comment.id}/like`, options)
    else router.put(`/comments/${comment.id}/like`, {}, options)
  }
  const remove = (id: string) => router.delete(`/comments/${id}`, {
    only: ['auth'],
    async: true,
    preserveScroll: true,
    preserveState: true,
    onSuccess: page => {
      if (refused(page)) return
      setVisible(items => items.filter(c => c.id !== id))
      patchComments(items => items.filter(c => c.id !== id))
      patchEntity('post', postId, post => ({ ...post, commentCount: Math.max(0, Number(post.commentCount ?? 0) - 1) }))
    },
  })

  return (
    <>
      <div ref={node => { scroll.current = node; measureMount(node) }} {...{ 'scroll-region': '' }} className="max-h-[60vh] overflow-y-auto outline-none focus-visible:ring-2 focus-visible:ring-ring/50" tabIndex={0} aria-label="Comments list">
        <ul style={mounted ? { height: virtual.getTotalSize(), position: 'relative' } : undefined}>
          {mounted ? virtual.getVirtualItems().map(row => (
            <li key={row.key} ref={virtual.measureElement} data-index={row.index} className="group absolute top-0 left-0 flex w-full gap-3 pb-5" style={{ transform: `translateY(${row.start}px)` }}>
              <CommentRow comment={visible[row.index]!} archived={archived} pending={pending.has(visible[row.index]!.id)} onLike={like} onRemove={remove} />
            </li>
          )) : visible.map(c => (
            <li key={c.id} className="group flex gap-3 pb-5">
              <CommentRow comment={c} archived={archived} pending={pending.has(c.id)} onLike={like} onRemove={remove} />
            </li>
          ))}
        </ul>
        {!visible.length && <EmptyState icon={MessageCircle} title="No comments yet" description="Start the conversation." className="py-10" />}
      </div>
      {error && <p role="alert" className="text-sm text-destructive">{error}</p>}
      {next && (
        <div className="flex justify-center">
          <Button variant="secondary" size="sm" className="rounded-full text-muted-foreground" disabled={loading} onClick={() => void more()}>
            {loading && <Loader2 className="animate-spin" />} Load older comments
          </Button>
        </div>
      )}
    </>
  )
}
