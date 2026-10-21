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
    <article className="group/card border-b py-7">
      {post.reposter && (
        <p className="mb-2 flex items-center gap-1.5 text-[13px] text-muted-foreground">
          <Repeat2 className="size-3.5" aria-hidden />
          <Link href={`/users/${post.reposter.id}`} className="font-medium hover:text-foreground">{post.reposter.name}</Link> reposted
        </p>
      )}

      <div className="flex items-center gap-2 text-[13px] text-muted-foreground">
        <Link href={`/users/${post.author.id}`} className="flex min-w-0 items-center gap-2 rounded-full text-foreground hover:underline">
          <UserAvatar user={post.author} className="size-5 text-[9px]" />
          <span className="truncate font-medium">{post.author.name}</span>
        </Link>
        <span aria-hidden>·</span>
        <time dateTime={post.createdAt} className="shrink-0">{shortDate(post.createdAt)}</time>
        {post.archivedAt && <span className="shrink-0 rounded bg-muted px-1.5 py-0.5 text-[11px] font-medium">Archived</span>}
      </div>
      {post.reason && <p className="mt-1 text-[13px] text-muted-foreground">{post.reason}</p>}

      <div className="mt-3 flex items-start gap-5">
        <Link href={href} prefetch="hover" cacheFor="30s" className="min-w-0 flex-1 rounded-sm">
          <h2 className="font-serif text-[1.45rem] leading-tight font-semibold tracking-[-0.015em] text-balance group-hover/card:underline group-hover/card:decoration-1 group-hover/card:underline-offset-4 sm:text-[1.65rem]">{post.title}</h2>
          {dek}
        </Link>
        {post.coverUrl && (
          <Link href={href} tabIndex={-1} aria-hidden className="shrink-0">
            <img src={post.coverUrl} alt="" loading="lazy" className="size-20 rounded-md bg-muted object-cover sm:h-24 sm:w-36" />
          </Link>
        )}
      </div>

      <div className="mt-4 flex items-center gap-3">
        {!!post.topics?.length && (
          <ul className="flex min-w-0 flex-wrap gap-1.5">
            {post.topics.slice(0, 2).map((topic, i) => <li key={topic} className={cn(i > 0 && 'hidden sm:block', 'rounded-full bg-muted px-2.5 py-1 text-xs text-muted-foreground')}>{topic}</li>)}
          </ul>
        )}
        <PostActionBar post={post} className="ml-auto" />
        <PostActions post={post} />
      </div>
    </article>
  )
}
