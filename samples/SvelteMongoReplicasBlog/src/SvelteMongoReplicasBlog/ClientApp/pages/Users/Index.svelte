<script lang="ts">
  import { InfiniteScroll, Link, router } from '@inertiajs/svelte'
  import { Search, UserRoundSearch, X } from '@lucide/svelte'
  import { onMount } from 'svelte'
  import EmptyState from '@/components/EmptyState.svelte'
  import FollowButton from '@/components/FollowButton.svelte'
  import LoadingRows from '@/components/LoadingRows.svelte'
  import PageHeader from '@/components/PageHeader.svelte'
  import PageTitle from '@/components/PageTitle.svelte'
  import Avatar from '@/components/ui/avatar/Avatar.svelte'
  import Button from '@/components/ui/button/Button.svelte'
  import Input from '@/components/ui/input/Input.svelte'
  import { useDebouncedSearch } from '@/lib/debounced-search.svelte'
  import { createWindowVirtualizer } from '@/lib/virtualizer.svelte'
  import type { ScrollPage, UserSummary } from '@/types'

  let { users, filters }: { users: ScrollPage<UserSummary>; filters: { q: string } } = $props()

  // svelte-ignore state_referenced_locally
  let q = $state(filters.q)
  useDebouncedSearch(() => q, '/users')

  let list: HTMLElement | undefined = $state()
  let end: HTMLElement | undefined = $state()
  let margin = $state(0)
  let mounted = $state(false)
  onMount(() => {
    mounted = true
    if (list) margin = list.getBoundingClientRect().top + window.scrollY
  })

  const virtualizer = createWindowVirtualizer<HTMLElement>(() => ({
    count: users.data.length,
    estimateSize: () => 81,
    overscan: 5,
    scrollMargin: margin,
    getItemKey: (index: number) => users.data[index]!.id,
  }))

  const rows = $derived(
    mounted
      ? virtualizer.virtualItems.map(row => ({ person: users.data[row.index]!, row }))
      : users.data.map(person => ({ person, row: null as { index: number; start: number } | null })),
  )
  const measure = (node: HTMLElement) => { if (mounted) virtualizer.measureElement(node) }

  // Rows from later pages ease in (staggered) the first time they appear; rows present on first render never animate.
  const fresh = new Map<string, number>()
  const done = new Set<string>()
  // svelte-ignore state_referenced_locally
  let lastLength = users.data.length
  $effect.pre(() => {
    const length = users.data.length
    if (length > lastLength) users.data.slice(lastLength).forEach((person, i) => fresh.set(String(person.id), Math.min(i, 6) * 60))
    lastLength = length
  })
  function rise(id: string) {
    const key = String(id)
    const delay = done.has(key) ? undefined : fresh.get(key)
    return { class: delay === undefined ? undefined : 'animate-rise', style: delay ? `animation-delay:${delay}ms` : undefined }
  }
  const markDone = (event: AnimationEvent, id: string) => { if (event.target === event.currentTarget) done.add(String(id)) }

  const initials = (name: string) => name.split(/\s+/).map(p => p[0]).slice(0, 2).join('').toUpperCase()
  const search = () => router.get('/users', q.trim() ? { q: q.trim() } : {}, { replace: true, preserveState: true, preserveScroll: true })
  const clear = () => { q = '' }
</script>

<PageTitle title="People" />
<section class="mx-auto flex w-full max-w-2xl flex-col gap-6">
  <PageHeader title="People" description="Find writers and follow the ones you like." />

  <form class="relative" role="search" onsubmit={(event: SubmitEvent) => { event.preventDefault(); search() }}>
    <Search class="text-muted-foreground pointer-events-none absolute top-1/2 left-3 size-4 -translate-y-1/2" aria-hidden="true" />
    <Input bind:value={q} aria-label="Search people" placeholder="Search people by name…" autocomplete="off" class="h-10 rounded-lg pr-10 pl-10" />
    {#if q}
      <button
        type="button" aria-label="Clear search"
        class="text-muted-foreground hover:text-foreground focus-visible:ring-ring absolute top-1/2 right-2 -translate-y-1/2 rounded-md p-1 outline-none focus-visible:ring-2"
        onclick={clear}
      >
        <X class="size-4" aria-hidden="true" />
      </button>
    {/if}
  </form>

  {#if users.data.length}
    <InfiniteScroll data="users" onlyNext preserveUrl buffer={600} endElement={() => end ?? null}>
      <div bind:this={list} role="list" aria-label="People" style={mounted ? `height:${virtualizer.totalSize}px;position:relative` : undefined}>
        {#each rows as { person, row } (person.id)}
          {@const animate = rise(person.id)}
          <div
            {@attach measure} data-index={row?.index} role="listitem" class="w-full"
            style={row ? `position:absolute;top:0;transform:translateY(${row.start - margin}px)` : undefined}
          >
            <div class={animate.class} style={animate.style} onanimationend={(event) => markDone(event, person.id)}>
              <article class="flex items-center gap-4 border-b py-4">
                <Link href={`/users/${person.id}`} aria-label={`View ${person.name}'s profile`} class="focus-visible:ring-ring shrink-0 rounded-full outline-none focus-visible:ring-2">
                  <Avatar src={person.avatarUrl} fallback={initials(person.name)} class="size-11" />
                </Link>
                <div class="min-w-0 flex-1">
                  <Link href={`/users/${person.id}`} class="block truncate font-medium hover:underline">{person.name}</Link>
                  <p class="text-muted-foreground truncate text-sm tabular-nums">{person.followerCount} followers · {person.followingCount} following</p>
                </div>
                <FollowButton user={person} />
              </article>
            </div>
          </div>
        {/each}
      </div>
      <div bind:this={end} class="h-px" aria-hidden="true"></div>
      {#snippet loading()}<LoadingRows kind="users" />{/snippet}
    </InfiniteScroll>
  {:else}
    <EmptyState
      icon={UserRoundSearch} title={filters.q ? `No people match “${filters.q}”` : 'No people yet'}
      description={filters.q ? 'Try a different name.' : 'People appear here once they join.'}
    >
      {#if filters.q}
        <Button variant="link" onclick={clear}>Clear search</Button>
      {/if}
    </EmptyState>
  {/if}
</section>
