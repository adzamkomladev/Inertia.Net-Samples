<script lang="ts">
  import { router, usePage } from '@inertiajs/svelte'
  import type { ReloadOptions } from '@inertiajs/core'
  import { Newspaper, PenLine, SearchX, X } from '@lucide/svelte'
  import EmptyState from '@/components/EmptyState.svelte'
  import PageHeader from '@/components/PageHeader.svelte'
  import PageTitle from '@/components/PageTitle.svelte'
  import VirtualPostList from '@/components/VirtualPostList.svelte'
  import Button from '@/components/ui/button/Button.svelte'
  import Skeleton from '@/components/ui/skeleton/Skeleton.svelte'
  import type { AuthUser, PostSummary, ScrollPage } from '@/types'

  let { posts, filters, searchEngine }: { posts: ScrollPage<PostSummary>; filters: { q: string }; searchEngine?: 'native' | 'meilisearch' } = $props()

  const page = usePage()
  const user = $derived((page.props.auth as { user: AuthUser | null } | undefined)?.user ?? null)
  let resetting = $state(false)

  const pagination = $derived<ReloadOptions>({
    only: ['searchEngine'],
    data: filters.q && searchEngine ? { engine: searchEngine } : {},
    onBeforeUpdate: page => {
      const actual = (page.props as { searchEngine?: 'native' | 'meilisearch' }).searchEngine
      if (filters.q && searchEngine && actual && actual !== searchEngine) {
        resetting = true
        ;(page.props as any).posts = { ...(page.props.posts as ScrollPage<PostSummary>), data: [] }
      }
    },
    onSuccess: page => {
      const actual = (page.props as { searchEngine?: 'native' | 'meilisearch' }).searchEngine
      if (resetting && actual) {
        router.get('/posts', { q: filters.q, engine: String(actual) }, {
          reset: ['posts'], replace: true, preserveState: false, preserveScroll: false,
          onFinish: () => { resetting = false },
        })
      }
    },
  })
</script>

<PageTitle title={filters.q ? `Results for “${filters.q}”` : 'Posts'} />
<div class="mx-auto max-w-2xl">
  <PageHeader description={filters.q ? 'Ranked by relevance across the whole community.' : 'Fresh writing from the community, newest first.'}>
    {#snippet title()}
      {#if filters.q}Results for <span class="font-serif italic">“{filters.q}”</span>{:else}Latest{/if}
    {/snippet}
    {#snippet actions()}
      {#if filters.q}
        <Button href="/posts" variant="outline" size="sm" class="rounded-full"><X class="size-4" /> Clear</Button>
      {:else if user?.verified}
        <Button href="/posts/create" size="sm" class="rounded-full lg:hidden"><PenLine class="size-4" /> Write</Button>
      {/if}
    {/snippet}
  </PageHeader>

  <div class="mt-4 border-t">
    {#if resetting}
      <div class="flex flex-col gap-6 py-5" aria-busy="true">
        {#each [0, 1, 2] as i (i)}
          <div class="flex gap-4">
            <Skeleton class="size-10 rounded-full" />
            <div class="flex-1 space-y-2.5"><Skeleton class="h-3.5 w-1/3" /><Skeleton class="h-5 w-4/5" /><Skeleton class="h-3.5 w-full" /></div>
          </div>
        {/each}
        <p role="status" class="sr-only">Refreshing search results…</p>
      </div>
    {:else if !posts.data.length}
      <EmptyState
        icon={filters.q ? SearchX : Newspaper}
        title={filters.q ? `No posts match “${filters.q}”` : 'No posts yet'}
        description={filters.q ? 'Try a different keyword, or browse everything.' : 'Be the first to publish something.'}
      >
        {#if filters.q}
          <Button href="/posts" variant="outline" size="sm" class="rounded-full">Show all posts</Button>
        {:else if user?.verified}
          <Button href="/posts/create" size="sm" class="rounded-full"><PenLine class="size-4" /> Write a post</Button>
        {/if}
      </EmptyState>
    {:else}
      {#key `${filters.q}:${searchEngine ?? 'native'}`}
        <VirtualPostList {posts} params={pagination} />
      {/key}
    {/if}
  </div>
</div>
