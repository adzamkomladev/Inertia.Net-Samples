<script lang="ts">
  import { InfiniteScroll, router } from '@inertiajs/svelte'
  import { CarFront, Search, SlidersHorizontal } from '@lucide/svelte'
  import LotCard from '@/components/LotCard.svelte'
  import PageHeader from '@/components/PageHeader.svelte'
  import PageTitle from '@/components/PageTitle.svelte'
  import Select from '@/components/ui/select/Select.svelte'
  import Skeleton from '@/components/ui/skeleton/Skeleton.svelte'
  import { useDebouncedSearch } from '@/lib/debounced-search.svelte'
  import type { LotCard as LotCardType, ScrollPage } from '@/types'

  /** GET /lots — deck directory with a Scroll feed. q/type/maxPrice/sort carry through partial reloads. */
  let {
    lots,
    filters,
  }: {
    lots: ScrollPage<LotCardType>
    filters: { q: string; type: string; maxPrice: number | null; sort: string }
  } = $props()

  let q = $state(filters.q)
  $effect.pre(() => { q = filters.q })

  useDebouncedSearch(() => q ?? '', '/lots', () => ({
    type: filters.type || '',
    maxPrice: filters.maxPrice ?? '',
    sort: filters.sort || 'name',
  }))

  const typeItems = [
    { value: '', label: 'Any spot type' },
    { value: 'ev', label: 'EV charging' },
    { value: 'handicap', label: 'Accessible' },
    { value: 'covered', label: 'Covered' },
    { value: 'compact', label: 'Compact' },
  ]
  const sortItems = [
    { value: 'name', label: 'Name A–Z' },
    { value: 'price', label: 'Cheapest first' },
    { value: 'price_desc', label: 'Priciest first' },
    { value: 'occupancy', label: 'Busiest first' },
  ]

  const apply = (patch: Record<string, string | number>) =>
    router.get(
      '/lots',
      { q: filters.q || '', type: filters.type || '', maxPrice: filters.maxPrice ?? '', sort: filters.sort || 'name', ...patch },
      { preserveState: true, preserveScroll: true },
    )

  const setMaxPrice = (event: Event) => {
    const value = (event.target as HTMLInputElement).value
    apply({ maxPrice: value })
  }

  const clearFilters = () => router.get('/lots', {}, { preserveScroll: true })
</script>

<PageTitle title="Find parking" description="Live decks with real-time free spot counts." />
<div class="mx-auto w-full max-w-6xl">
  <PageHeader title="Find parking" description="Live decks with real-time free spot counts." />

  <!-- Filter rail -->
  <div class="bg-card shadow-card mt-5 flex flex-col gap-3 rounded-2xl border p-3 sm:flex-row sm:items-center">
    <div class="relative flex-1">
      <Search class="text-muted-foreground pointer-events-none absolute top-1/2 left-3 size-4 -translate-y-1/2" aria-hidden="true" />
      <input
        type="search"
        bind:value={q}
        placeholder="Search by name, area or address…"
        aria-label="Search lots"
        class="placeholder:text-muted-foreground focus-visible:border-ring focus-visible:ring-ring/30 h-10 w-full rounded-xl border border-input bg-transparent pr-3 pl-9 text-sm outline-none focus-visible:ring-2"
      />
    </div>
    <div class="grid grid-cols-2 gap-2 sm:flex sm:items-center sm:gap-2">
      <Select items={typeItems} value={filters.type || ''} onValueChange={(v) => apply({ type: v })} ariaLabel="Spot type" placeholder="Any spot type" class="h-10 w-full sm:w-40" />
      <Select items={sortItems} value={filters.sort || 'name'} onValueChange={(v) => apply({ sort: v })} ariaLabel="Sort" placeholder="Sort" class="h-10 w-full sm:w-36" />
      <label class="border-input focus-within:border-ring focus-within:ring-ring/30 flex h-10 items-center gap-2 rounded-xl border bg-transparent px-3 text-sm outline-none focus-within:ring-2">
        <SlidersHorizontal class="text-muted-foreground size-3.5" aria-hidden="true" />
        <span class="text-muted-foreground hidden text-xs whitespace-nowrap lg:block">Max $/h</span>
        <input
          type="number"
          min="50"
          step="50"
          value={filters.maxPrice ?? ''}
          onchange={setMaxPrice}
          placeholder="—"
          aria-label="Max price per hour in cents"
          class="font-mono w-16 bg-transparent tabular-nums outline-none [&::-webkit-inner-spin-button]:appearance-none"
        />
      </label>
    </div>
  </div>

  <!-- Results -->
  <div class="mt-6">
    {#if lots.data.length}
      <InfiniteScroll data="lots" onlyNext preserveUrl buffer={600}>
        <div class="grid gap-5 sm:grid-cols-2 xl:grid-cols-3">
          {#each lots.data as lot (lot.id)}
            <LotCard {lot} />
          {/each}
        </div>
        {#snippet loading()}
          <div class="grid gap-5 sm:grid-cols-2 xl:grid-cols-3">
            {#each [0, 1, 2] as i (i)}
              <Skeleton class="h-72 rounded-3xl" />
            {/each}
          </div>
        {/snippet}
      </InfiniteScroll>
    {:else}
      <div class="animate-rise flex flex-col items-center gap-4 px-6 py-16 text-center">
        <span class="bg-card text-muted-foreground shadow-card flex size-14 items-center justify-center rounded-2xl border">
          <CarFront class="size-6" strokeWidth={1.5} aria-hidden="true" />
        </span>
        <div class="max-w-sm space-y-1.5">
          <p class="font-display text-base font-semibold">No decks match</p>
          <p class="text-muted-foreground text-sm leading-6">
            {filters.q ? `Nothing found for “${filters.q}”.` : 'No lots match these filters yet.'}
          </p>
        </div>
        <button type="button" onclick={clearFilters} class="text-cta-foreground bg-cta rounded-xl px-4 py-2 text-sm font-medium transition-transform active:scale-95">
          Clear search &amp; filters
        </button>
      </div>
    {/if}
  </div>
</div>
