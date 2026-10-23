import { Link } from '@inertiajs/react'
import { Repeat2 } from 'lucide-react'
import { cn } from 'cn'
import { UserAvatar } from '@/layouts/AppLayout'
import type { PostCard } from '@/types/posts'
import PostActionBar from './PostActionBar'
import PostActions from './PostActions'
import { shortDate } from './time'

/** Feed row that reads like an index of essays: byline, serif title, dek, optional thumbnail, quiet meta line. */
export default function PostCardItem({ post }: { post: PostCard }) {
  const href = `/posts/${post.id}`
  const dek = post.snippet ? (
    // The server HTML-encodes the snippet and only restores <mark> tags.
    <p className="mt-1.5 line-clamp-2 text-[15.5px] leading-relaxed text-muted-foreground [&_mark]:rounded-sm [&_mark]:bg-brand/20 [&_mark]:px-0.5 [&_mark]:text-foreground" dangerouslySetInnerHTML={{ __html: post.snippet }} />
  ) : post.excerpt && <p className="mt-1.5 line-clamp-2 text-[15.5px] leading-relaxed text-muted-foreground">{post.excerpt}</p>

  return (
    <article className="group/card relative -mx-3 rounded-2xl px-3 py-7 transition-colors duration-300 after:pointer-events-none after:absolute after:inset-x-3 after:bottom-0 after:h-px after:bg-border hover:bg-accent/45 hover:after:opacity-0 sm:-mx-4 sm:px-4 sm:after:inset-x-4">
      <Link href={href} prefetch="hover" cacheFor="30s" aria-label={`Read ${post.title}`} className="absolute inset-0 z-0 rounded-xl focus-visible:ring-2 focus-visible:ring-ring" />
      {post.reposter && (
        <p className="relative z-10 mb-2 flex items-center gap-1.5 text-[13px] text-muted-foreground pointer-events-none">
          <Repeat2 className="size-3.5" aria-hidden />
          <Link href={`/users/${post.reposter.id}`} className="pointer-events-auto font-medium hover:text-foreground">{post.reposter.name}</Link> reposted
        </p>
      )}

      <div className="relative z-10 flex items-center gap-2 text-[13px] text-muted-foreground pointer-events-none">
        <Link href={`/users/${post.author.id}`} className="pointer-events-auto flex min-w-0 items-center gap-2 rounded-full text-foreground hover:underline">
          <UserAvatar user={post.author} className="size-5 text-[9px]" />
          <span className="truncate font-medium">{post.author.name}</span>
        </Link>
        <span aria-hidden>·</span>
        <time dateTime={post.createdAt} className="shrink-0">{shortDate(post.createdAt)}</time>
        {post.archivedAt && <span className="shrink-0 rounded bg-muted px-1.5 py-0.5 text-[11px] font-medium">Archived</span>}
      </div>
      {post.reason && <p className="relative z-10 mt-1 text-[13px] text-muted-foreground pointer-events-none">{post.reason}</p>}

      <div className="relative z-10 mt-3 flex items-start gap-5 pointer-events-none">
        <div className="min-w-0 flex-1 rounded-sm">
          <h2 className="font-serif text-[1.45rem] leading-tight font-semibold tracking-[-0.015em] text-balance decoration-brand/60 decoration-1 underline-offset-4 group-hover/card:underline sm:text-[1.65rem]">{post.title}</h2>
          {dek}
        </div>
        {post.coverUrl && (
          <div aria-hidden className="shrink-0">
            <img src={post.coverUrl} alt="" loading="lazy" className="size-20 rounded-lg bg-muted object-cover transition-transform duration-500 group-hover/card:scale-[1.03] sm:h-24 sm:w-36" />
          </div>
        )}
      </div>

      <div className="relative z-10 mt-4 flex items-center gap-3 pointer-events-none">
        {!!post.topics?.length && (
          <ul className="flex min-w-0 flex-wrap gap-1.5">
            {post.topics.slice(0, 2).map((topic, i) => (
              <li key={topic} className={cn(i > 0 && 'hidden sm:block')}>
                <Link href={`/posts?topic=${encodeURIComponent(topic)}`} className="pointer-events-auto block rounded-full bg-muted px-2.5 py-1 text-xs text-muted-foreground transition-colors hover:bg-foreground hover:text-background">
                  {topic}
                </Link>
              </li>
            ))}
          </ul>
        )}
        <PostActionBar post={post} className="pointer-events-auto ml-auto" />
        <div className="pointer-events-auto"><PostActions post={post} /></div>
      </div>
    </article>
  )
}
