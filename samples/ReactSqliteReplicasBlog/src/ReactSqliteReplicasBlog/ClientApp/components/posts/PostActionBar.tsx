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

type Tone = 'comment' | 'repost' | 'like' | 'bookmark'

const tones: Record<Tone, { hover: string; active: string }> = {
  comment: { hover: 'hover:text-comment [&:hover>span:first-child]:bg-comment/10', active: 'text-comment' },
  repost: { hover: 'hover:text-repost [&:hover>span:first-child]:bg-repost/10', active: 'text-repost' },
  like: { hover: 'hover:text-like [&:hover>span:first-child]:bg-like/10', active: 'text-like' },
  bookmark: { hover: 'hover:text-bookmark [&:hover>span:first-child]:bg-bookmark/10', active: 'text-bookmark' },
}

function ActionButton({ label, count, active, tone, icon: Icon, onClick, disabled, href }: {
  label: string
  count?: number
  active?: boolean
  tone: Tone
  icon: typeof Heart
  onClick?: () => void
  disabled?: boolean
  href?: string
}) {
  // Only animate after a click, never when a liked post first renders.
  const [bumped, setBumped] = useState(false)
  const body = (
    <>
      <span className="flex size-8 items-center justify-center rounded-full transition-colors">
        <Icon
          className={cn('size-[18px] transition-transform', active && 'fill-current', bumped && active && 'animate-pop')}
          strokeWidth={1.75} onAnimationEnd={() => setBumped(false)} aria-hidden
        />
      </span>
      <span className="min-w-5 overflow-hidden text-left text-[13px] tabular-nums">
        {!!count && <span key={count} className="inline-block animate-tick">{count}</span>}
      </span>
      <span className="sr-only">{label}</span>
    </>
  )
  const className = cn(
    '-ml-2 flex items-center rounded-full pr-1 text-muted-foreground transition-colors disabled:pointer-events-none disabled:opacity-50',
    tones[tone].hover, active && tones[tone].active,
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

/** Comment / repost / like / bookmark row used by post cards and the post page. */
export default function PostActionBar({ post, showBookmark = true, className }: { post: ActionPost; showBookmark?: boolean; className?: string }) {
  const user = usePage<SharedProps>().props.auth.user
  const verified = !!user?.verified
  const like = useSocialToggle({ url: `/posts/${post.id}/like`, id: post.id, field: 'liked', active: !!post.liked, count: post.likeCount, countField: 'likeCount' })
  const repost = useSocialToggle({ url: `/posts/${post.id}/repost`, id: post.id, field: 'reposted', active: !!post.reposted, count: post.repostCount, countField: 'repostCount' })
  const bookmark = useSocialToggle({ url: `/posts/${post.id}/bookmark`, id: post.id, field: 'bookmarked', active: !!post.bookmarked })
  const row = cn('flex items-center gap-5 sm:gap-8', className)
  const comments = <ActionButton label={post.commentCount ? `${post.commentCount} comments` : 'Comment'} count={post.commentCount} tone="comment" icon={MessageCircle} href={`/posts/${post.id}#comments`} />

  if (!user) {
    return (
      <div className={row}>
        {comments}
        <ActionButton label="Log in to repost" count={post.repostCount} tone="repost" icon={Repeat2} href="/login" />
        <ActionButton label="Log in to like" count={post.likeCount} tone="like" icon={Heart} href="/login" />
        {showBookmark && <span className="ml-auto"><ActionButton label="Log in to bookmark" tone="bookmark" icon={Bookmark} href="/login" /></span>}
      </div>
    )
  }

  const requireVerified = (action: () => void) => verified ? action : () => router.visit('/email/verify')
  return (
    <div className={row}>
      {comments}
      <ActionButton
        label={!verified ? 'Verify email to repost' : repost.active ? 'Undo repost' : 'Repost'} count={repost.count} active={repost.active}
        tone="repost" icon={Repeat2} disabled={!!post.archivedAt || repost.pending} onClick={requireVerified(repost.toggle)}
      />
      <ActionButton
        label={!verified ? 'Verify email to like' : like.active ? 'Unlike' : 'Like'} count={like.count} active={like.active}
        tone="like" icon={Heart} disabled={!!post.archivedAt || like.pending} onClick={requireVerified(like.toggle)}
      />
      {showBookmark && (
        <span className="ml-auto">
          <ActionButton
            label={bookmark.active ? 'Remove bookmark' : 'Bookmark'} active={bookmark.active}
            tone="bookmark" icon={Bookmark} disabled={bookmark.pending} onClick={bookmark.toggle}
          />
        </span>
      )}
    </div>
  )
}
