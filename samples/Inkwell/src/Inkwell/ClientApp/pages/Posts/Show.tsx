import { Link, router, useForm, usePage, Deferred } from '@inertiajs/react'
import { BarChart3, Check, Ellipsis, Loader2, Pencil, Share2, Trash2 } from 'lucide-react'
import { useState, type FormEvent, type KeyboardEvent } from 'react'
import { toast } from 'sonner'
import { cn } from 'cn'
import FollowButton from '@/components/posts/FollowButton'
import Markdown from '@/components/posts/Markdown'
import PostActionBar from '@/components/posts/PostActionBar'
import VirtualComments from '@/components/posts/VirtualComments'
import { fullDate, readMinutes } from '@/components/posts/time'
import {
  AlertDialog, AlertDialogAction, AlertDialogCancel, AlertDialogContent, AlertDialogDescription,
  AlertDialogFooter, AlertDialogHeader, AlertDialogTitle, AlertDialogTrigger,
} from '@/components/ui/alert-dialog'
import { Avatar, AvatarFallback, AvatarImage } from '@/components/ui/avatar'
import { DropdownMenu, DropdownMenuContent, DropdownMenuItem, DropdownMenuSeparator, DropdownMenuTrigger } from '@/components/ui/dropdown-menu'
import { Button } from '@/components/ui/button'
import { Skeleton } from '@/components/ui/skeleton'
import { Textarea } from '@/components/ui/textarea'
import AppLayout, { initials } from '@/layouts/AppLayout'
import { useViewBeacon } from '@/hooks/use-view-beacon'
import type { PostComment, PostDetail, UserSummary } from '@/types/posts'
import type { SharedProps } from '@/types/shared'

interface Props {
  post: PostDetail
  liked: boolean
  canEdit: boolean
  /** Deferred: present after the follow-up request that loads the comments group. */
  comments?: PostComment[]
  /** Deferred group "author": the closing author card. */
  author?: UserSummary | null
}

/** Copies the post link (or opens the native share sheet on phones) and confirms with a check mark. */
function ShareButton({ title }: { title: string }) {
  const [copied, setCopied] = useState(false)
  const share = async () => {
    const url = window.location.href.split('#')[0]!
    try {
      if (navigator.share && matchMedia('(pointer: coarse)').matches) {
        await navigator.share({ title, url })
        return
      }
      await navigator.clipboard.writeText(url)
      setCopied(true)
      toast.success('Link copied')
      setTimeout(() => setCopied(false), 1800)
    } catch (err) {
      if ((err as Error)?.name !== 'AbortError') toast.error('Could not share this link')
    }
  }
  return (
    <Button variant="ghost" size="icon" className="size-11 rounded-full text-muted-foreground" aria-label={copied ? 'Link copied' : 'Share post'} onClick={share}>
      {copied ? <Check className="animate-pop text-repost" /> : <Share2 />}
    </Button>
  )
}

function AuthorCard({ author }: { author: UserSummary }) {
  return (
    <aside aria-label="About the author" className="flex animate-rise flex-col gap-4 rounded-2xl border bg-card p-6 sm:flex-row sm:items-start">
      <Link href={`/users/${author.id}`} className="shrink-0 rounded-full">
        <Avatar className="size-14">
          {author.avatarUrl && <AvatarImage src={author.avatarUrl} alt="" />}
          <AvatarFallback>{initials(author.name)}</AvatarFallback>
        </Avatar>
      </Link>
      <div className="min-w-0 flex-1 space-y-1.5">
        <p className="text-xs font-medium tracking-wide text-muted-foreground uppercase">Written by</p>
        <Link href={`/users/${author.id}`} className="block font-serif text-xl font-semibold hover:underline">{author.name}</Link>
        {author.bio && <p className="text-[15px] leading-relaxed text-muted-foreground">{author.bio}</p>}
        <p className="text-sm text-muted-foreground tabular-nums">{author.followerCount.toLocaleString()} {author.followerCount === 1 ? 'follower' : 'followers'}</p>
      </div>
      {!author.isSelf && <FollowButton user={author} className="self-start" />}
    </aside>
  )
}

function CommentsSkeleton() {
  return (
    <div className="divide-y" aria-busy="true">
      {[0, 1, 2].map(i => (
        <div key={i} className="flex gap-3 py-4">
          <Skeleton className="size-9 shrink-0 rounded-full" />
          <div className="flex-1 space-y-2">
            <Skeleton className="h-3.5 w-32" />
            <Skeleton className="h-3.5 w-full" />
            <Skeleton className="h-3.5 w-2/3" />
          </div>
        </div>
      ))}
    </div>
  )
}

function OwnerMenu({ base, onDelete }: { base: string; onDelete: () => void }) {
  return (
    <AlertDialog>
      <DropdownMenu>
        <DropdownMenuTrigger asChild>
          <Button variant="ghost" size="icon" className="size-11 rounded-full text-muted-foreground" aria-label="Post options"><Ellipsis /></Button>
        </DropdownMenuTrigger>
        <DropdownMenuContent align="end">
          <DropdownMenuItem asChild><Link href={`${base}/edit`}><Pencil /> Edit</Link></DropdownMenuItem>
          <DropdownMenuItem asChild><Link href={`${base}/analytics`}><BarChart3 /> Analytics</Link></DropdownMenuItem>
          <DropdownMenuSeparator />
          <AlertDialogTrigger asChild><DropdownMenuItem variant="destructive" onSelect={e => e.preventDefault()}><Trash2 /> Delete</DropdownMenuItem></AlertDialogTrigger>
        </DropdownMenuContent>
      </DropdownMenu>
      <AlertDialogContent>
        <AlertDialogHeader>
          <AlertDialogTitle>Delete this post?</AlertDialogTitle>
          <AlertDialogDescription>This permanently removes the post and its comments.</AlertDialogDescription>
        </AlertDialogHeader>
        <AlertDialogFooter>
          <AlertDialogCancel>Cancel</AlertDialogCancel>
          <AlertDialogAction onClick={onDelete}>Delete</AlertDialogAction>
        </AlertDialogFooter>
      </AlertDialogContent>
    </AlertDialog>
  )
}

export default function Show({ post, liked, canEdit, comments, author }: Props) {
  const user = usePage<SharedProps>().props.auth.user
  const form = useForm({ body: '' })
  const base = `/posts/${post.id}`
  useViewBeacon(post.id)

  const remove = () => router.delete(base)
  const comment = (e?: FormEvent) => {
    e?.preventDefault()
    if (form.processing || !form.data.body.trim()) return
    form.post(`${base}/comments`, {
      preserveScroll: true, preserveState: true,
      onSuccess: () => { form.reset(); toast.success('Response posted') },
    })
  }
  // Ctrl/Cmd+Enter sends, like most writing tools.
  const sendOnShortcut = (e: KeyboardEvent<HTMLTextAreaElement>) => {
    if (e.key === 'Enter' && (e.metaKey || e.ctrlKey)) { e.preventDefault(); comment() }
  }

  return (
    <AppLayout title={post.title}>
      <div aria-hidden className="reading-progress fixed inset-x-0 top-0 z-40 h-0.5 bg-brand" />
      <article className="flex flex-col gap-10">
        <header className="flex flex-col gap-6">
          <h1 className="font-serif text-[2.35rem] leading-[1.1] font-semibold tracking-[-0.025em] text-balance sm:text-[3.1rem]">{post.title}</h1>

          <div className="flex items-center gap-3">
            <Link href={`/users/${post.author.id}`} className="shrink-0 rounded-full">
              <Avatar className="size-11">
                {post.author.avatarUrl && <AvatarImage src={post.author.avatarUrl} alt="" />}
                <AvatarFallback className="text-xs font-medium">{initials(post.author.name)}</AvatarFallback>
              </Avatar>
            </Link>
            <div className="flex min-w-0 flex-col text-sm">
              <Link href={`/users/${post.author.id}`} className="truncate font-medium hover:underline">{post.author.name}</Link>
              <p className="text-muted-foreground">
                <time dateTime={post.createdAt}>{fullDate(post.createdAt)}</time>
                <span aria-hidden> · </span>{readMinutes(post.body)} min read
              </p>
            </div>
            <div className="ml-auto flex items-center">
              <ShareButton title={post.title} />
              {canEdit && <OwnerMenu base={base} onDelete={remove} />}
            </div>
          </div>

          {post.archivedAt && (
            <p className="rounded-md bg-muted px-3 py-2 text-sm text-muted-foreground">This post is archived and only visible to you.</p>
          )}
        </header>

        {post.coverUrl && (
          <figure>
            <img src={post.coverUrl} alt="" className="aspect-[16/9] w-full rounded-lg bg-muted object-cover" />
          </figure>
        )}

        <Markdown>{post.body}</Markdown>

        {!!post.topics?.length && (
          <ul className="flex flex-wrap gap-2">
            {post.topics.map(topic => (
              <li key={topic}>
                <Link href={`/posts?topic=${encodeURIComponent(topic)}`} className="block rounded-full bg-muted px-3 py-1.5 text-sm text-muted-foreground transition-colors hover:bg-foreground hover:text-background">{topic}</Link>
              </li>
            ))}
          </ul>
        )}

        <div className="sticky bottom-[calc(4.5rem+env(safe-area-inset-bottom))] z-20 mx-auto rounded-full border bg-background/90 px-2 shadow-lg backdrop-blur-md lg:bottom-6">
          <PostActionBar post={{ ...post, liked: post.liked ?? liked }} className="gap-1" />
        </div>

        <Deferred data="author" fallback={<Skeleton className="h-32 w-full rounded-2xl" />}>
          {author ? <AuthorCard author={author} /> : <></>}
        </Deferred>

        <section id="comments" aria-labelledby="comments-heading" className="flex scroll-mt-20 flex-col gap-6 border-t pt-10">
          <h2 id="comments-heading" className="font-serif text-2xl font-semibold">Responses <span className="font-sans text-base font-normal text-muted-foreground tabular-nums">{post.commentCount}</span></h2>
          {post.archivedAt ? (
            <p className="text-sm text-muted-foreground">Comments are closed while this post is archived.</p>
          ) : user?.verified ? (
            <form onSubmit={comment} className="flex gap-3" noValidate>
              <Avatar className="mt-0.5 size-9">
                {user.avatarUrl && <AvatarImage src={user.avatarUrl} alt="" />}
                <AvatarFallback className="text-xs">{initials(user.name)}</AvatarFallback>
              </Avatar>
              <div className="flex min-w-0 flex-1 flex-col gap-2">
                <Textarea
                  aria-label="Add a comment" placeholder="Add to the conversation" rows={2} maxLength={5000} value={form.data.body}
                  aria-invalid={!!form.errors.body} onChange={(e) => form.setData('body', e.target.value)} onKeyDown={sendOnShortcut}
                  className="field-auto max-h-60 min-h-20 resize-none rounded-xl text-[15px]"
                />
                {form.errors.body && <p role="alert" className="text-sm text-destructive">{form.errors.body}</p>}
                <div className="flex items-center justify-between gap-3">
                  <p className="text-xs text-muted-foreground">
                    <span className="hidden sm:inline"><kbd className="rounded border px-1 font-sans">Ctrl</kbd> + <kbd className="rounded border px-1 font-sans">Enter</kbd> to send · </span>
                    <span className={cn('tabular-nums', form.data.body.length > 4800 && 'text-destructive')}>{form.data.body.length.toLocaleString()}/5,000</span>
                  </p>
                  <Button type="submit" size="sm" className="rounded-full active:scale-[0.98]" disabled={form.processing || !form.data.body.trim()}>
                    {form.processing && <Loader2 className="animate-spin" />} {form.processing ? 'Posting…' : 'Reply'}
                  </Button>
                </div>
              </div>
            </form>
          ) : user ? (
            <p className="text-sm text-muted-foreground">
              Verify your email to join the conversation. <Link href="/email/verify" className="text-brand underline underline-offset-4">Verify now</Link>
            </p>
          ) : (
            <p className="text-sm text-muted-foreground">
              <Link href="/login" className="text-brand underline underline-offset-4">Log in</Link> to join the conversation.
            </p>
          )}

          <Deferred data="comments" fallback={<CommentsSkeleton />}>
            <VirtualComments postId={post.id} comments={comments ?? []} archived={!!post.archivedAt} />
          </Deferred>
        </section>
      </article>
    </AppLayout>
  )
}
