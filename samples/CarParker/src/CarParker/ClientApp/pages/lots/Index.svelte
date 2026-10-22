<script lang="ts">
  import { InfiniteScroll, router } from '@inertiajs/svelte'
  import Search from '@lucide/svelte/icons/search'
  import SearchX from '@lucide/svelte/icons/search-x'
  import EmptyState from '@/components/EmptyState.svelte'
  import LotCard from '@/components/LotCard.svelte'
  import PageHeader from '@/components/PageHeader.svelte'
  import PageTitle from '@/components/PageTitle.svelte'
  import Button from '@/components/ui/button/Button.svelte'
  import Label from '@/components/ui/label/Label.svelte'
  import Select from '@/components/ui/select/Select.svelte'
  import Skeleton from '@/components/ui/skeleton/Skeleton.svelte'
  import { useDebouncedSearch } from '@/lib/debounced-search.svelte'
  import type { LotCard as LotCardType, ScrollPage } from '@/types'

  /** GET /lots: lots with price and live availability up front. q/type/maxPrice/sort carry through partial reloads. */
  let {
    lots,
    filters,
  }: {
    lots: ScrollPage<LotCardType>
    filters: { q: string; type: string; maxPrice: number | null; sort: string }
  } = $props()

  // svelte-ignore state_referenced_locally
  let q = $state(filters.q)
  $effect.pre(() => { q = filters.q })

  useDebouncedSearch(() => q ?? '', '/lots', () => ({
    type: filters.type || '',
    maxPrice: filters.maxPrice ?? '',
    sort: filters.sort || 'name',
  }))

  const typeItems = [
    { value: 'any', label: 'Any bay type' },
    { value: 'ev', label: 'EV charging' },
    { value: 'handicap', label: 'Accessible' },
    { value: 'covered', label: 'Covered' },
    { value: 'compact', label: 'Compact' },
  ]
  const sortItems = [
    { value: 'name', label: 'Name A to Z' },
    { value: 'price', label: 'Cheapest first' },
    { value: 'price_desc', label: 'Priciest first' },
    { value: 'occupancy', label: 'Busiest first' },
  ]

  const apply = (patch: Record<string, string | number>) =>
    router.get(
      '/lots',
      { q: q || '', type: filters.type || '', maxPrice: filters.maxPrice ?? '', sort: filters.sort || 'name', ...patch },
      { preserveState: true, preserveScroll: true },
    )

  const clearFilters = () => router.get('/lots', {}, { preserveScroll: true })
</script>

<PageTitle title="Find parking" description="Parking lots with live availability and hourly prices." />
<div class="mx-auto w-full max-w-6xl px-4 pt-8 sm:px-6">
  <PageHeader title="Find parking" description="Prices are per hour with a daily maximum. Availability is live." />

  <div class="mt-6 grid items-start gap-6 lg:grid-cols-[17rem_minmax(0,1fr)]">
  <form class="bg-card shadow-card grid gap-5 rounded-xl border p-5 lg:sticky lg:top-24" role="search" onsubmit={(event: SubmitEvent) => event.preventDefault()}>
    <h2 class="font-semibold text-lg">Find your space</h2>
    <div class="grid gap-1.5">
      <Label for="lot-search">Area, address or lot name</Label>
      <div class="relative">
        <Search class="text-muted-foreground pointer-events-none absolute top-1/2 left-3 size-4 -translate-y-1/2" aria-hidden="true" />
        <input
          id="lot-search"
          type="search"
          bind:value={q}
          placeholder="Search"
          class="bg-card ring-input placeholder:text-muted-foreground focus-visible:ring-ring h-11 w-full rounded-lg pr-3 pl-9 text-base ring-1 ring-inset outline-none focus-visible:ring-2 md:h-10 md:text-sm"
        />
      </div>
    </div>
    <div class="grid gap-1.5">
      <Label for="lot-type">Bay type</Label>
      <Select id="lot-type" items={typeItems} value={filters.type || 'any'} onValueChange={(v) => apply({ type: v === 'any' ? '' : v })} ariaLabel="Bay type" class="w-full" />
    </div>
    <div class="grid gap-1.5">
      <Label for="lot-sort">Sort by</Label>
      <Select id="lot-sort" items={sortItems} value={filters.sort || 'name'} onValueChange={(v) => apply({ sort: v })} ariaLabel="Sort" class="w-full" />
    </div>
    <div class="border-t pt-4"><Button variant="outline" class="w-full" onclick={clearFilters}>Reset filters</Button></div>
    <p class="text-muted-foreground text-xs leading-5">Compare hourly prices and live availability. Open a lot to choose your bay.</p>
  </form>

  <div>
    <p class="text-muted-foreground mb-4 text-sm" role="status">{lots.data.length} parking locations loaded{filters.q ? ` for “${filters.q}”` : ''}</p>
    {#if lots.data.length}
      <InfiniteScroll data="lots" onlyNext preserveUrl buffer={600}>
        <div class="grid gap-4 sm:grid-cols-2">
          {#each lots.data as lot (lot.id)}
            <LotCard {lot} />
          {/each}
        </div>
        {#snippet loading()}
          <div class="mt-4 grid gap-4 sm:grid-cols-2">
            {#each [0, 1, 2] as i (i)}
              <Skeleton class="h-56 rounded-2xl" />
            {/each}
          </div>
        {/snippet}
      </InfiniteScroll>
    {:else}
      <EmptyState
        icon={SearchX}
        title="No lots match"
        description={filters.q ? `Nothing found for "${filters.q}". Try another area or clear the filters.` : 'No lots match these filters. Clear them to see every lot.'}
      >
        <Button onclick={clearFilters}>Clear search and filters</Button>
      </EmptyState>
    {/if}
  </div>
  </div>
</div>
