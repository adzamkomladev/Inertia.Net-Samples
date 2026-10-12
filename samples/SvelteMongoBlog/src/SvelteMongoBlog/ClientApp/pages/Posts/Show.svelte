<script lang="ts">
  import { Deferred, Link, router, useForm, usePage } from '@inertiajs/svelte'
  import { BarChart3, Loader2, Pencil, Trash2 } from '@lucide/svelte'
  import CommentList from '@/components/CommentList.svelte'
  import PageTitle from '@/components/PageTitle.svelte'
  import PostActionBar from '@/components/PostActionBar.svelte'
  import ConfirmDialog from '@/components/ui/alert-dialog/ConfirmDialog.svelte'
  import Avatar from '@/components/ui/avatar/Avatar.svelte'
  import Button from '@/components/ui/button/Button.svelte'
  import Skeleton from '@/components/ui/skeleton/Skeleton.svelte'
  import Textarea from '@/components/ui/textarea/Textarea.svelte'
  import { renderMarkdown } from '@/lib/markdown'
  import { useViewBeacon } from '@/lib/view-beacon.svelte'
  import type { AuthUser, CommentItem, PostDetail } from '@/types'

  let { post, comments }: { post: PostDetail; comments?: CommentItem[] } = $props()

  const page = usePage()
  const user = $derived((page.props.auth as { user: AuthUser | null } | undefined)?.user ?? null)
  const html = $derived(renderMarkdown(post.body))
  const date = $derived(new Intl.DateTimeFormat('en', { dateStyle: 'long', timeZone: 'UTC' }).format(new Date(post.createdAt)))
  const readMinutes = $derived(Math.max(1, Math.round(post.body.trim().split(/\s+/).length / 230)))
  const initials = (name: string) => name.split(/\s+/).map(p => p[0]).slice(0, 2).join('').toUpperCase()
  // Count the view after mount (not on GET) so prefetched/cached visits are counted too; analytics must never break reading.
  useViewBeacon(() => post.id)
  const remove = () => router.delete(`/posts/${post.id}`)

  const form = useForm({ body: '' })
  const comment = () =>
    form.post(`/posts/${post.id}/comments`, { preserveScroll: true, preserveState: true, only: ['post', 'comments'], onSuccess: () => form.reset() })
</script>

<PageTitle title={post.title} description={post.excerpt} />
<article class="animate-rise mx-auto flex max-w-2xl flex-col gap-8">
  <header class="flex flex-col gap-5">
    {#if post.coverUrl}
      <img src={post.coverUrl} alt={`Cover image for ${post.title}`} class="bg-muted aspect-[16/9] w-full rounded-xl border object-cover" />
    {/if}

    <h1 class="font-serif text-4xl leading-tight font-semibold tracking-tight text-balance sm:text-[2.6rem]">{post.title}</h1>

    <div class="flex flex-wrap items-center gap-x-3 gap-y-3">
      <Link href={`/users/${post.author.id}`} class="focus-visible:ring-ring shrink-0 rounded-full outline-none focus-visible:ring-2">
        <Avatar src={post.author.avatarUrl} fallback={initials(post.author.name)} class="size-10" fallbackClass="text-xs font-medium" />
      </Link>
      <div class="flex min-w-0 flex-col text-sm">
        <Link href={`/users/${post.author.id}`} class="truncate font-medium hover:underline">{post.author.name}</Link>
        <p class="text-muted-foreground">
          <time datetime={post.createdAt}>{date}</time>
          <span aria-hidden="true"> · </span>{readMinutes} min read
          <span aria-hidden="true"> · </span><span class="tabular-nums">{post.viewCount}</span> views
        </p>
      </div>

      {#if post.canEdit}
        <div class="ml-auto flex items-center gap-1">
          <Button href={`/posts/${post.slug}/edit`} variant="ghost" size="sm" class="text-muted-foreground rounded-full"><Pencil /> Edit</Button>
          <Button href={`/analytics/posts/${post.id}`} variant="ghost" size="sm" class="text-muted-foreground rounded-full"><BarChart3 /> Analytics</Button>
          <ConfirmDialog title="Delete this post?" description="This permanently removes the post and its comments." onConfirm={remove} triggerClass="text-muted-foreground hover:bg-destructive/10 hover:text-destructive inline-flex h-8 items-center gap-1.5 rounded-full px-3 text-sm font-medium">
            {#snippet trigger()}<Trash2 class="size-4" /> Delete{/snippet}
          </ConfirmDialog>
        </div>
      {/if}
    </div>

    {#if post.archivedAt}
      <p class="bg-muted text-muted-foreground rounded-lg px-3 py-2 text-sm">This post is archived and only visible to you.</p>
    {/if}
  </header>

  <div class="prose-post break-words">{@html html}</div>

  {#if post.topics?.length}
    <div class="flex flex-wrap gap-x-3 gap-y-1">
      {#each post.topics as topic (topic)}<span class="text-muted-foreground text-sm">#{topic}</span>{/each}
    </div>
  {/if}

  <div class="border-y py-1.5">
    <PostActionBar {post} />
  </div>

  <section id="comments" aria-labelledby="comments-heading" class="flex scroll-mt-20 flex-col gap-5">
    <h2 id="comments-heading" class="text-base font-semibold">Comments <span class="text-muted-foreground font-normal tabular-nums">{post.commentCount}</span></h2>

    {#if post.archivedAt}
      <p class="text-muted-foreground text-sm">Comments are closed while this post is archived.</p>
    {:else if user?.verified}
      <form class="flex gap-3" novalidate onsubmit={(event: SubmitEvent) => { event.preventDefault(); comment() }}>
        <Avatar src={user.avatarUrl} fallback={initials(user.name)} class="mt-0.5 size-9" fallbackClass="text-xs" />
        <div class="flex min-w-0 flex-1 flex-col gap-2">
          <Textarea
            bind:value={form.body} autosize maxHeight={240} aria-label="Add a comment" placeholder="Add to the conversation" rows="2" maxlength="5000"
            aria-invalid={!!form.errors.body} class="field-sizing-fixed max-h-60 min-h-20 resize-none rounded-xl text-[15px]"
          />
          {#if form.errors.body}<p role="alert" class="text-destructive text-sm">{form.errors.body}</p>{/if}
          <div class="flex justify-end">
            <Button type="submit" size="sm" class="rounded-full active:scale-[0.98]" disabled={form.processing || !form.body.trim()}>
              {#if form.processing}<Loader2 class="animate-spin" />{/if} Reply
            </Button>
          </div>
        </div>
      </form>
    {:else if user}
      <p class="text-muted-foreground text-sm">
        Verify your email to join the conversation. <Link href="/email/verify" class="text-brand underline underline-offset-4">Verify now</Link>
      </p>
    {:else}
      <p class="text-muted-foreground text-sm">
        <Link href="/login" class="text-brand underline underline-offset-4">Log in</Link> to join the conversation.
      </p>
    {/if}

    <Deferred data="comments">
      {#snippet fallback()}
        <div class="divide-y" aria-busy="true">
          {#each [0, 1, 2] as i (i)}
            <div class="flex gap-3 py-4">
              <Skeleton class="size-9 shrink-0 rounded-full" />
              <div class="flex-1 space-y-2">
                <Skeleton class="h-3.5 w-32" />
                <Skeleton class="h-3.5 w-full" />
                <Skeleton class="h-3.5 w-2/3" />
              </div>
            </div>
          {/each}
        </div>
      {/snippet}
      {#key post.id}
        <CommentList postId={post.id} comments={comments ?? []} archived={!!post.archivedAt} />
      {/key}
    </Deferred>
  </section>
</article>
