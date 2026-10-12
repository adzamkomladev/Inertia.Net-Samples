import { Link, router, useForm, usePage, Deferred } from '@inertiajs/react'
import { BarChart3, Loader2, Pencil, Trash2 } from 'lucide-react'
import type { FormEvent } from 'react'
import Markdown from '@/components/posts/Markdown'
import PostActionBar from '@/components/posts/PostActionBar'
import VirtualComments from '@/components/posts/VirtualComments'
import { fullDate } from '@/components/posts/time'
import {
  AlertDialog, AlertDialogAction, AlertDialogCancel, AlertDialogContent, AlertDialogDescription,
  AlertDialogFooter, AlertDialogHeader, AlertDialogTitle, AlertDialogTrigger,
} from '@/components/ui/alert-dialog'
import { Avatar, AvatarFallback, AvatarImage } from '@/components/ui/avatar'
import { Button } from '@/components/ui/button'
import { Skeleton } from '@/components/ui/skeleton'
import { Textarea } from '@/components/ui/textarea'
import AppLayout, { initials } from '@/layouts/AppLayout'
import { useViewBeacon } from '@/hooks/use-view-beacon'
import type { PostComment, PostDetail } from '@/types/posts'
import type { SharedProps } from '@/types/shared'

interface Props {
  post: PostDetail
  liked: boolean
  canEdit: boolean
  /** Deferred: present after the follow-up request that loads the comments group. */
  comments?: PostComment[]
}

const readMinutes = (body: string) => Math.max(1, Math.round(body.trim().split(/\s+/).length / 230))

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

export default function Show({ post, liked, canEdit, comments }: Props) {
  const user = usePage<SharedProps>().props.auth.user
  const form = useForm({ body: '' })
  const base = `/posts/${post.id}`
  useViewBeacon(post.id)

  const remove = () => router.delete(base)
  const comment = (e: FormEvent) => {
    e.preventDefault()
    form.post(`${base}/comments`, { preserveScroll: true, preserveState: true, onSuccess: () => form.reset() })
  }

  return (
    <AppLayout title={post.title}>
      <article className="mx-auto flex max-w-2xl animate-rise flex-col gap-8">
        <header className="flex flex-col gap-5">
          {post.coverUrl && <img src={post.coverUrl} alt="" className="aspect-[16/9] w-full rounded-xl border bg-muted object-cover" />}

          <h1 className="font-serif text-4xl leading-tight font-semibold tracking-tight text-balance sm:text-[2.6rem]">{post.title}</h1>

          <div className="flex flex-wrap items-center gap-x-3 gap-y-3">
            <Link href={`/users/${post.author.id}`} className="shrink-0 rounded-full outline-none focus-visible:ring-2 focus-visible:ring-ring">
              <Avatar className="size-10">
                {post.author.avatarUrl && <AvatarImage src={post.author.avatarUrl} alt="" />}
                <AvatarFallback className="text-xs font-medium">{initials(post.author.name)}</AvatarFallback>
              </Avatar>
            </Link>
            <div className="flex min-w-0 flex-col text-sm">
              <Link href={`/users/${post.author.id}`} className="truncate font-medium hover:underline">{post.author.name}</Link>
              <p className="text-muted-foreground">
                <time dateTime={post.createdAt}>{fullDate(post.createdAt)}</time>
                <span aria-hidden> · </span>{readMinutes(post.body)} min read
                <span aria-hidden> · </span><span className="tabular-nums">{post.viewCount}</span> views
              </p>
            </div>

            {canEdit && (
              <div className="ml-auto flex items-center gap-1">
                <Button asChild variant="ghost" size="sm" className="rounded-full text-muted-foreground"><Link href={`${base}/edit`}><Pencil /> Edit</Link></Button>
                <Button asChild variant="ghost" size="sm" className="rounded-full text-muted-foreground"><Link href={`${base}/analytics`}><BarChart3 /> Analytics</Link></Button>
                <AlertDialog>
                  <AlertDialogTrigger asChild>
                    <Button variant="ghost" size="sm" className="rounded-full text-muted-foreground hover:bg-destructive/10 hover:text-destructive"><Trash2 /> Delete</Button>
                  </AlertDialogTrigger>
                  <AlertDialogContent>
                    <AlertDialogHeader>
                      <AlertDialogTitle>Delete this post?</AlertDialogTitle>
                      <AlertDialogDescription>This permanently removes the post and its comments.</AlertDialogDescription>
                    </AlertDialogHeader>
                    <AlertDialogFooter>
                      <AlertDialogCancel>Cancel</AlertDialogCancel>
                      <AlertDialogAction onClick={remove}>Delete</AlertDialogAction>
                    </AlertDialogFooter>
                  </AlertDialogContent>
                </AlertDialog>
              </div>
            )}
          </div>

          {post.archivedAt && (
            <p className="rounded-lg bg-muted px-3 py-2 text-sm text-muted-foreground">This post is archived and only visible to you.</p>
          )}
        </header>

        <Markdown>{post.body}</Markdown>

        {!!post.topics?.length && (
          <div className="flex flex-wrap gap-x-3 gap-y-1">
            {post.topics.map(topic => <span key={topic} className="text-sm text-muted-foreground">#{topic}</span>)}
          </div>
        )}

        <div className="border-y py-1.5">
          <PostActionBar post={{ ...post, liked: post.liked ?? liked }} />
        </div>

        <section id="comments" aria-labelledby="comments-heading" className="flex scroll-mt-20 flex-col gap-5">
          <h2 id="comments-heading" className="text-base font-semibold">Comments <span className="font-normal text-muted-foreground tabular-nums">{post.commentCount}</span></h2>

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
                  aria-invalid={!!form.errors.body} onChange={(e) => form.setData('body', e.target.value)}
                  className="max-h-60 min-h-20 resize-none rounded-xl text-[15px]"
                />
                {form.errors.body && <p role="alert" className="text-sm text-destructive">{form.errors.body}</p>}
                <div className="flex justify-end">
                  <Button type="submit" size="sm" className="rounded-full active:scale-[0.98]" disabled={form.processing || !form.data.body.trim()}>
                    {form.processing && <Loader2 className="animate-spin" />} Reply
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
