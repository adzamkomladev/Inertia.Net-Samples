<script lang="ts">
  import { Link, router, useHttp, usePage } from '@inertiajs/svelte'
  import { Heart, Loader2, MessageCircle, Trash2 } from '@lucide/svelte'
  import { onDestroy, onMount, untrack } from 'svelte'
  import { SvelteSet } from 'svelte/reactivity'
  import Avatar from '@/components/ui/avatar/Avatar.svelte'
  import Button from '@/components/ui/button/Button.svelte'
  import ConfirmDialog from '@/components/ui/alert-dialog/ConfirmDialog.svelte'
  import EmptyState from '@/components/EmptyState.svelte'
  import RelativeTime from '@/components/RelativeTime.svelte'
  import { refused, patchEntityProps } from '@/lib/social-toggle'
  import { createElementVirtualizer } from '@/lib/virtualizer.svelte'
  import { cn } from '@/lib/utils'
  import type { AuthUser, CommentItem } from '@/types'

  let { postId, comments, archived = false }: { postId: string; comments: CommentItem[]; archived?: boolean } = $props()

  const page = usePage()
  const user = $derived((page.props.auth as { user: AuthUser | null } | undefined)?.user ?? null)

  // svelte-ignore state_referenced_locally
  let visible = $state<CommentItem[]>(comments.map(c => ({ ...c })))
  let scroll: HTMLElement | undefined = $state()
  let mounted = $state(false)
  onMount(() => (mounted = true))

  let nextCursor = $state<string | null>(null)
  const pagination = useHttp<Record<string, never>, { data: CommentItem[]; nextCursor: string | null }>({})
  const loading = $derived(pagination.processing)
  let paginationStarted = false
  let loadError = $state('')
  onDestroy(() => pagination.cancel())

  const pending = new SvelteSet<string>()
  // Only animate after a click, never when a liked comment first renders.
  let bumped = $state<string | null>(null)

  $effect(() => {
    const incoming = comments
    untrack(() => {
      const byId = new Map(visible.map(c => [c.id, c]))
      incoming.forEach(c => byId.set(c.id, { ...c }))
      visible = [...byId.values()].sort((a, b) => b.createdAt.localeCompare(a.createdAt) || b.id.localeCompare(a.id))
      // Fresh deferred props may update likes or add replies, but must not restart an exhausted cursor.
      if (!paginationStarted) nextCursor = incoming.length === 50 ? incoming[49]!.cursor ?? null : null
    })
  })

  const virtualizer = createElementVirtualizer<HTMLElement, HTMLElement>(() => ({
    count: visible.length,
    getScrollElement: () => scroll ?? null,
    estimateSize: () => 120,
    overscan: 5,
    getItemKey: (index: number) => visible[index]!.id,
  }))

  const rows = $derived(
    mounted
      ? virtualizer.virtualItems.map(row => ({ comment: visible[row.index]!, row }))
      : visible.map(comment => ({ comment, row: null as { index: number; start: number } | null })),
  )
  const measure = (node: HTMLElement) => { if (mounted) virtualizer.measureElement(node) }

  async function loadMore() {
    if (!nextCursor || loading) return
    paginationStarted = true
    loadError = ''
    try {
      const result = await pagination.get(`/posts/${postId}/comments?cursor=${encodeURIComponent(nextCursor)}`)
      const known = new Set(visible.map(c => c.id))
      visible.push(...result.data.filter(c => !known.has(c.id)))
      nextCursor = result.nextCursor
    } catch { loadError = 'Unable to load older comments. Please try again.' }
  }

  // Keep the `comments` prop (and so the history snapshot) in sync with what the server confirmed.
  const patchComments = (update: (items: CommentItem[]) => CommentItem[]) =>
    router.replaceProp('comments', (items: unknown) => Array.isArray(items) ? update(items as CommentItem[]) : items)

  const like = (comment: CommentItem) => {
    if (pending.has(comment.id)) return
    pending.add(comment.id)
    const previous = { liked: comment.liked, count: comment.likeCount ?? 0 }
    const next = { liked: !previous.liked, likeCount: Math.max(0, previous.count + (previous.liked ? -1 : 1)) }
    let confirmed = false
    Object.assign(comment, next)
    bumped = comment.id
    const options = {
      only: ['auth'], async: true, preserveScroll: true, preserveState: true,
      onSuccess: (page: { flash?: unknown }) => {
        if (refused(page)) return
        confirmed = true
        patchComments(items => items.map(c => c.id === comment.id ? { ...c, ...next } : c))
      },
      onFinish: () => {
        pending.delete(comment.id)
        if (!confirmed) Object.assign(comment, { liked: previous.liked, likeCount: previous.count })
      },
    }
    const url = `/comments/${comment.id}/like`
    if (previous.liked) router.delete(url, options)
    else router.put(url, {}, options)
  }

  const remove = (id: string) => router.delete(`/comments/${id}`, {
    only: ['auth'],
    async: true,
    preserveScroll: true,
    preserveState: true,
    onSuccess: (page: { flash?: unknown }) => {
      if (refused(page)) return
      visible = visible.filter(c => c.id !== id)
      patchComments(items => items.filter(c => c.id !== id))
      router.replace({
        preserveState: true,
        preserveScroll: true,
        props: (old: Record<string, any>) => patchEntityProps(old, {
          kind: 'post',
          id: postId,
          patch: post => ({ ...post, commentCount: Math.max(0, Number(post.commentCount ?? 0) - 1) }),
        }),
      })
    },
  })

  const initials = (n: string) => n.split(/\s+/).map(p => p[0]).slice(0, 2).join('').toUpperCase()
</script>

{#if !visible.length}
  <EmptyState icon={MessageCircle} title="No comments yet" description="Start the conversation." class="py-10" />
{:else}
  <!-- svelte-ignore a11y_no_noninteractive_tabindex -->
  <div bind:this={scroll} tabindex="0" aria-label="Comments list" class="focus-visible:ring-ring/50 max-h-[60vh] overflow-y-auto outline-none focus-visible:ring-2">
    <ul style={mounted ? `height:${virtualizer.totalSize}px;position:relative` : undefined}>
      {#each rows as { comment, row } (comment.id)}
        <li
          id={`comment-${comment.id}`}
          {@attach measure}
          data-index={row?.index}
          class="group flex w-full scroll-mt-20 gap-3 pb-5"
          style={row ? `position:absolute;top:0;left:0;transform:translateY(${row.start}px)` : undefined}
        >
          <Avatar src={comment.author.avatarUrl} fallback={initials(comment.author.name)} class="size-9 shrink-0" fallbackClass="text-xs" />
          <div class="min-w-0 flex-1">
            <div class="flex items-center gap-1.5 text-sm">
              <Link href={`/users/${comment.author.id}`} class="truncate font-medium hover:underline">{comment.author.name}</Link>
              <span aria-hidden="true" class="text-muted-foreground/60">·</span>
              <RelativeTime iso={comment.createdAt} class="text-muted-foreground shrink-0" />
              {#if comment.canDelete && !archived}
                <ConfirmDialog
                  title="Delete this comment?"
                  description="This cannot be undone."
                  onConfirm={() => remove(comment.id)}
                  triggerClass="text-muted-foreground hover:bg-destructive/10 hover:text-destructive ml-auto rounded-full focus-visible:opacity-100 [@media(hover:hover)]:opacity-0 [@media(hover:hover)]:group-hover:opacity-100"
                >
                  {#snippet trigger()}
                    <span class="inline-flex size-6 items-center justify-center rounded-md" aria-label={`Delete comment by ${comment.author.name}`}>
                      <Trash2 class="size-3" />
                    </span>
                  {/snippet}
                </ConfirmDialog>
              {/if}
            </div>
            <p class="mt-0.5 text-[15px] leading-relaxed break-words whitespace-pre-wrap">{comment.body}</p>
            <button
              type="button" disabled={archived || !user?.verified} aria-busy={pending.has(comment.id)} aria-pressed={!!comment.liked}
              class={cn('group/like text-muted-foreground hover:text-like focus-visible:ring-ring -ml-1.5 mt-1 flex items-center gap-1 rounded-full pr-2 text-xs transition-colors outline-none focus-visible:ring-2 disabled:pointer-events-none disabled:opacity-50', comment.liked && 'text-like')}
              onclick={() => like(comment)}
            >
              <span class="group-hover/like:bg-like/10 flex size-6 items-center justify-center rounded-full transition-colors">
                <Heart
                  class={cn('size-3.5', comment.liked && 'fill-current', bumped === comment.id && comment.liked && 'animate-pop')}
                  aria-hidden="true" onanimationend={() => (bumped = null)}
                />
              </span>
              <span class="tabular-nums">{comment.likeCount || ''}</span>
              <span class="sr-only">{comment.liked ? 'Unlike comment' : 'Like comment'}</span>
            </button>
          </div>
        </li>
      {/each}
    </ul>
  </div>
{/if}
{#if loadError}<p role="alert" class="text-destructive text-sm">{loadError}</p>{/if}
{#if nextCursor}
  <div class="flex justify-center">
    <Button variant="secondary" size="sm" class="text-muted-foreground rounded-full" disabled={loading} onclick={loadMore}>
      {#if loading}<Loader2 class="animate-spin" />{/if} Load older comments
    </Button>
  </div>
{/if}
