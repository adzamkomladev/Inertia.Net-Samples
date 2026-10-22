<script lang="ts">
  import { Link, router } from '@inertiajs/svelte'
  import ArrowRight from '@lucide/svelte/icons/arrow-right'
  import CarFront from '@lucide/svelte/icons/car-front'
  import Search from '@lucide/svelte/icons/search'
  import ShieldCheck from '@lucide/svelte/icons/shield-check'
  import EmptyState from '@/components/EmptyState.svelte'
  import LotCard from '@/components/LotCard.svelte'
  import PageTitle from '@/components/PageTitle.svelte'
  import type { LotCard as LotCardType } from '@/types'

  /** GET /: search first, then the lots with space right now. Public and SSR'd. */
  let { lots }: { lots: LotCardType[] } = $props()

  const steps = [
    ['Pick a bay', 'Open a lot, check the live floor plan and tap a numbered bay: EV, covered or accessible.'],
    ['Choose your hours', 'One visit, or the same hours every week for up to eight weeks. The price is shown before you pay.'],
    ['Drive in', 'Pay by card, mobile money or at the gate. The guard matches your plate to the booking.'],
  ]

  const totalFree = $derived(lots.reduce((sum, lot) => sum + lot.free, 0))

  const search = (event: SubmitEvent) => {
    event.preventDefault()
    const q = String(new FormData(event.currentTarget as HTMLFormElement).get('q') ?? '').trim()
    router.get('/lots', q ? { q } : {})
  }
</script>

<PageTitle />

<section class="bg-primary text-primary-foreground">
  <div class="mx-auto w-full max-w-6xl px-4 py-14 sm:px-6 sm:py-20">
    <h1 class="max-w-2xl text-4xl font-bold tracking-tight text-balance sm:text-6xl">Reserve a parking bay before you arrive.</h1>
    <form class="mt-8 flex max-w-xl flex-col gap-2 sm:flex-row" role="search" onsubmit={search}>
      <label class="relative flex-1">
        <span class="sr-only">Area, address or lot name</span>
        <Search class="text-muted-foreground pointer-events-none absolute top-1/2 left-4 size-5 -translate-y-1/2" aria-hidden="true" />
        <input
          type="search"
          name="q"
          placeholder="Area, address or lot name"
          class="bg-card text-foreground placeholder:text-muted-foreground h-12 w-full rounded-xl pr-4 pl-12 text-base outline-none focus-visible:ring-2 focus-visible:ring-white/70"
        />
      </label>
      <button type="submit" class="bg-foreground text-background hover:bg-foreground/90 h-12 rounded-xl px-6 text-base font-semibold transition-colors focus-visible:ring-2 focus-visible:ring-white/70 focus-visible:outline-none">
        Find parking
      </button>
    </form>
    {#if lots.length}
      <p class="text-primary-foreground/80 mt-5 text-sm tabular-nums">{totalFree} bays free right now across {lots.length} lots</p>
    {/if}
  </div>
</section>

<section class="mx-auto w-full max-w-6xl px-4 pt-12 sm:px-6" aria-labelledby="lots-title">
  <div class="flex items-end justify-between gap-4">
    <h2 id="lots-title" class="text-2xl font-bold">Lots with space now</h2>
    <Link href="/lots" prefetch="hover" class="text-primary inline-flex min-h-11 items-center gap-1 font-semibold whitespace-nowrap hover:underline underline-offset-4">
      All lots <ArrowRight class="size-4" aria-hidden="true" />
    </Link>
  </div>
  {#if lots.length}
    <div class="mt-4 grid gap-4 sm:grid-cols-2 lg:grid-cols-3">
      {#each lots.slice(0, 6) as lot (lot.id)}
        <LotCard {lot} />
      {/each}
    </div>
  {:else}
    <EmptyState icon={CarFront} title="No lots are open yet" description="Lots appear here as soon as they open for bookings." />
  {/if}
</section>

<section class="mx-auto w-full max-w-6xl px-4 pt-16 sm:px-6" aria-labelledby="how-title">
  <h2 id="how-title" class="text-2xl font-bold">How it works</h2>
  <ol class="mt-6 grid gap-6 sm:grid-cols-3">
    {#each steps as [title, body], i (title)}
      <li class="flex gap-4">
        <span class="bg-primary/10 text-primary font-display grid size-9 shrink-0 place-items-center rounded-full text-lg font-bold">{i + 1}</span>
        <div>
          <h3 class="text-lg font-bold">{title}</h3>
          <p class="text-muted-foreground mt-1 leading-6">{body}</p>
        </div>
      </li>
    {/each}
  </ol>
  <p class="bg-card shadow-card mt-8 flex items-start gap-3 rounded-xl p-4 leading-6">
    <ShieldCheck class="text-primary mt-0.5 size-5 shrink-0" aria-hidden="true" />
    <span><strong>Spot Cover</strong> is optional on every booking: 12% of the parking charge (minimum $2) covers damage or theft up to the lot's limit.</span>
  </p>
</section>
