import { Link, router, usePage } from '@inertiajs/react'
import { Bookmark, Heart, MessageCircle, Repeat2 } from 'lucide-react'
import { useState } from 'react'
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

const tones = { like: 'text-like', repost: 'text-repost', bookmark: 'text-bookmark', comment: 'text-comment' }

function ActionButton({ label, count, active, icon: Icon, tone, onClick, disabled, href }: {
  label: string
  count?: number
  active?: boolean
  icon: typeof Heart
  tone: 'like' | 'repost' | 'bookmark' | 'comment'
  onClick?: () => void
  disabled?: boolean
  href?: string
}) {
  // Only animate after a click, never when a liked post first renders.
  const [bumped, setBumped] = useState(false)
  const body = (
    <>
      <Icon
        className={cn('size-[18px] transition-transform', tones[tone], active && 'fill-current', bumped && active && 'animate-pop')}
        strokeWidth={1.75} onAnimationEnd={() => setBumped(false)} aria-hidden
      />
      {!!count && <span key={count} className="min-w-3 animate-tick text-[13px] tabular-nums">{count}</span>}
      <span className="sr-only">{label}</span>
    </>
  )
  const className = cn(
    'flex h-10 min-w-10 items-center justify-center gap-1.5 rounded-full px-2.5 transition-colors hover:bg-accent disabled:pointer-events-none disabled:opacity-50',
    tones[tone],
  )
  if (href) return <Link href={href} className={className} aria-label={label}>{body}</Link>
  return (
    <button
      type="button" className={className} aria-label={label} aria-pressed={active} disabled={disabled}
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
  const comments = <ActionButton label={post.commentCount ? `${post.commentCount} comments` : 'Comment'} count={post.commentCount} icon={MessageCircle} tone="comment" href={`/posts/${post.id}#comments`} />

  if (!user) {
    return (
      <div className={row}>
        {comments}
        <ActionButton label="Log in to repost" count={post.repostCount} icon={Repeat2} tone="repost" href="/login" />
        <ActionButton label="Log in to like" count={post.likeCount} icon={Heart} tone="like" href="/login" />
        {showBookmark && <ActionButton label="Log in to bookmark" icon={Bookmark} tone="bookmark" href="/login" />}
      </div>
    )
  }

  const requireVerified = (action: () => void) => verified ? action : () => router.visit('/email/verify')
  return (
    <div className={row}>
      {comments}
      <ActionButton
        label={!verified ? 'Verify email to repost' : repost.active ? 'Undo repost' : 'Repost'} count={repost.count} active={repost.active}
        icon={Repeat2} tone="repost" disabled={!!post.archivedAt || repost.pending} onClick={requireVerified(repost.toggle)}
      />
      <ActionButton
        label={!verified ? 'Verify email to like' : like.active ? 'Unlike' : 'Like'} count={like.count} active={like.active}
        icon={Heart} tone="like" disabled={!!post.archivedAt || like.pending} onClick={requireVerified(like.toggle)}
      />
      {showBookmark && (
        <ActionButton
          label={bookmark.active ? 'Remove bookmark' : 'Bookmark'} active={bookmark.active}
          icon={Bookmark} tone="bookmark" disabled={bookmark.pending} onClick={bookmark.toggle}
        />
      )}
    </div>
  )
}
