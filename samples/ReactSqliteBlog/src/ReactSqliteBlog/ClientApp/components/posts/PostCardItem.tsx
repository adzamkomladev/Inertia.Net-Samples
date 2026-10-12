import { Link } from '@inertiajs/react'
import { Eye, Repeat2 } from 'lucide-react'
import { Avatar, AvatarFallback, AvatarImage } from '@/components/ui/avatar'
import { initials } from '@/layouts/AppLayout'
import type { PostCard } from '@/types/posts'
import PostActionBar from './PostActionBar'
import PostActions from './PostActions'
import { timeAgo } from './time'

const compact = new Intl.NumberFormat('en', { notation: 'compact' })

/** Feed row: avatar rail, author line, serif title, excerpt, cover and actions. Rows are separated by a hairline. */
export default function PostCardItem({ post }: { post: PostCard }) {
  const href = `/posts/${post.id}`
  return (
    <article className="group/card relative flex gap-3.5 border-b py-5 sm:gap-4">
      <Link href={`/users/${post.author.id}`} className="relative shrink-0 self-start rounded-full">
        <Avatar className="size-10">
          {post.author.avatarUrl && <AvatarImage src={post.author.avatarUrl} alt="" />}
          <AvatarFallback className="text-xs font-medium">{initials(post.author.name)}</AvatarFallback>
        </Avatar>
      </Link>

      <div className="min-w-0 flex-1">
        {post.reposter && (
          <p className="mb-1 flex items-center gap-1.5 text-xs text-muted-foreground">
            <Repeat2 className="size-3.5 text-repost" aria-hidden />
            <Link href={`/users/${post.reposter.id}`} className="font-medium hover:text-foreground">{post.reposter.name}</Link> reposted
          </p>
        )}

        <div className="flex items-center gap-1.5 text-sm">
          <Link href={`/users/${post.author.id}`} className="truncate font-medium hover:underline">{post.author.name}</Link>
          <span aria-hidden className="text-muted-foreground/60">·</span>
          <time dateTime={post.createdAt} className="shrink-0 text-muted-foreground">{timeAgo(post.createdAt)}</time>
          {post.archivedAt && <span className="shrink-0 rounded-md bg-muted px-1.5 py-0.5 text-[11px] font-medium text-muted-foreground">Archived</span>}
          <span className="ml-auto flex shrink-0 items-center gap-1">
            <span className="flex items-center gap-1 text-xs text-muted-foreground tabular-nums" title={`${post.viewCount} views`}>
              <Eye className="size-3.5" aria-hidden />
              {compact.format(post.viewCount)}
              <span className="sr-only"> views</span>
            </span>
            <PostActions post={post} />
          </span>
        </div>
        {post.reason && <p className="text-xs text-muted-foreground">{post.reason}</p>}

        <Link href={href} prefetch="hover" cacheFor="30s" className="mt-1.5 block rounded-md">
          <h2 className="font-serif text-[21px] leading-snug font-semibold tracking-[-0.01em] text-balance transition-colors group-hover/card:text-foreground/80">{post.title}</h2>
          {post.snippet ? (
            // The server HTML-encodes the snippet and only restores <mark> tags.
            <p
              className="mt-1 line-clamp-2 text-[15px] leading-6 text-muted-foreground [&_mark]:rounded-sm [&_mark]:bg-brand/20 [&_mark]:px-0.5 [&_mark]:text-foreground"
              dangerouslySetInnerHTML={{ __html: post.snippet }}
            />
          ) : (
            post.excerpt && <p className="mt-1 line-clamp-2 text-[15px] leading-6 text-muted-foreground">{post.excerpt}</p>
          )}
          {post.coverUrl && (
            <div className="mt-3 overflow-hidden rounded-xl border bg-muted">
              <img src={post.coverUrl} alt="" loading="lazy" className="aspect-[16/9] w-full object-cover transition-transform duration-500 group-hover/card:scale-[1.015]" />
            </div>
          )}
        </Link>

        {!!post.topics?.length && (
          <div className="mt-2.5 flex flex-wrap gap-x-3 gap-y-1">
            {post.topics.map(topic => (
              <span key={topic} className="text-[13px] text-muted-foreground">#{topic}</span>
            ))}
          </div>
        )}

        <PostActionBar post={post} className="mt-2" />
      </div>
    </article>
  )
}
