import { Link, router, usePage } from '@inertiajs/react'
import { Bookmark, Heart, MessageCircle, Repeat2 } from 'lucide-react'
import { useState } from 'react'
import { toast } from 'sonner'
import { cn } from 'cn'
import { useSocialToggle } from '@/hooks/use-social-mutation'
import type { SocialPost } from '@/types/posts'
import type { SharedProps } from '@/types/shared'

type ActionPost = SocialPost & {
  commentCount?: number
  likeCount?: number
  repostCount?: number
  liked?: boolean
}

// One accent: actions rest in the muted ink and only take colour once they are yours. A like turns terracotta,
// a repost or bookmark turns full-strength ink, so a list of essays is not a wall of four competing hues.
const activeTone = { like: 'text-brand', repost: 'text-foreground', bookmark: 'text-foreground', comment: 'text-foreground' }

function plural(count: number, one: string) {
  return `${count.toLocaleString()} ${count === 1 ? one : `${one}s`}`
}

function ActionButton({ label, count, noun, active, icon: Icon, tone, onClick, disabled, href }: {
  /** A stable verb ("Like"); the pressed state is announced by aria-pressed, never by swapping the label. */
  label: string
  count?: number
  /** What the count counts, for the accessible name ("Like, 3 likes"). */
  noun?: string
  active?: boolean
  icon: typeof Heart
  tone: keyof typeof activeTone
  onClick?: () => void
  disabled?: boolean
  href?: string
}) {
  // Only animate after a click, never when a liked post first renders.
  const [bumped, setBumped] = useState(false)
  const name = count && noun ? `${label}, ${plural(count, noun)}` : label
  const body = (
    <>
      <Icon
        className={cn('size-[18px] transition-transform', active && 'fill-current', bumped && active && 'animate-pop')}
        strokeWidth={1.75} onAnimationEnd={() => setBumped(false)} aria-hidden
      />
      {!!count && <span key={count} className="min-w-3 animate-tick text-[13px] tabular-nums" aria-hidden>{count}</span>}
    </>
  )
  const className = cn(
    'flex h-10 min-w-10 items-center justify-center gap-1.5 rounded-full px-2.5 text-muted-foreground transition-colors hover:bg-accent hover:text-foreground disabled:pointer-events-none disabled:opacity-50',
    active && activeTone[tone],
  )
  if (href) return <Link href={href} className={className} aria-label={name}>{body}</Link>
  return (
    <button
      type="button" className={className} aria-label={name} aria-pressed={active} disabled={disabled}
      onClick={() => { setBumped(true); onClick?.() }}
    >
      {body}
    </button>
  )
}

/** Comment / repost / like / bookmark buttons, shared by post rows and the sticky bar on the post page. */
export default function PostActionBar({ post, showBookmark = true, className }: { post: ActionPost; showBookmark?: boolean; className?: string }) {
  const user = usePage<SharedProps>().props.auth.user
  const verified = !!user?.verified
  const like = useSocialToggle({ url: `/posts/${post.id}/like`, id: post.id, field: 'liked', active: !!post.liked, count: post.likeCount, countField: 'likeCount' })
  const repost = useSocialToggle({ url: `/posts/${post.id}/repost`, id: post.id, field: 'reposted', active: !!post.reposted, count: post.repostCount, countField: 'repostCount' })
  const bookmark = useSocialToggle({ url: `/posts/${post.id}/bookmark`, id: post.id, field: 'bookmarked', active: !!post.bookmarked })
  const row = cn('flex items-center', className)
  const comments = <ActionButton label="Comments" noun="comment" count={post.commentCount} icon={MessageCircle} tone="comment" href={`/posts/${post.id}#comments`} />

  if (!user) {
    return (
      <div className={row}>
        {comments}
        <ActionButton label="Log in to repost" noun="repost" count={post.repostCount} icon={Repeat2} tone="repost" href="/login" />
        <ActionButton label="Log in to like" noun="like" count={post.likeCount} icon={Heart} tone="like" href="/login" />
        {showBookmark && <ActionButton label="Log in to save" icon={Bookmark} tone="bookmark" href="/login" />}
      </div>
    )
  }

  const requireVerified = (action: () => void) => verified ? action : () => router.visit('/email/verify')
  const saveToggle = () => {
    const saving = !bookmark.active
    bookmark.toggle()
    // A bookmark changes nothing else on screen, so confirm it and say where saved essays live.
    if (saving) toast('Saved for later', { action: { label: 'View saved', onClick: () => router.visit('/bookmarks') } })
  }
  return (
    <div className={row}>
      {comments}
      <ActionButton
        label={verified ? 'Repost' : 'Verify email to repost'} noun="repost" count={repost.count} active={repost.active}
        icon={Repeat2} tone="repost" disabled={!!post.archivedAt || repost.pending} onClick={requireVerified(repost.toggle)}
      />
      <ActionButton
        label={verified ? 'Like' : 'Verify email to like'} noun="like" count={like.count} active={like.active}
        icon={Heart} tone="like" disabled={!!post.archivedAt || like.pending} onClick={requireVerified(like.toggle)}
      />
      {showBookmark && (
        <ActionButton
          label="Save" active={bookmark.active}
          icon={Bookmark} tone="bookmark" disabled={bookmark.pending} onClick={saveToggle}
        />
      )}
    </div>
  )
}
