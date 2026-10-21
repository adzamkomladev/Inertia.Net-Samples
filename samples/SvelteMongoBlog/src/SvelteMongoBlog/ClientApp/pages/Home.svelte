<script lang="ts">
  import { Link } from '@inertiajs/svelte'
  import { ArrowRight, CarFront, CalendarRange, CreditCard, MapPin, ShieldCheck, Timer } from '@lucide/svelte'
  import LotCard from '@/components/LotCard.svelte'
  import PageTitle from '@/components/PageTitle.svelte'
  import Button from '@/components/ui/button/Button.svelte'
  import type { LotCard as LotCardType } from '@/types'

  /** GET / — hero, live lot carousel, how-it-works band. Public and SSR'd. */
  let { lots }: { lots: LotCardType[] } = $props()
</script>

<PageTitle />

<!-- Hero -->
<section class="bg-primary relative overflow-hidden">
  <div class="deck-grid absolute inset-0 opacity-50" aria-hidden="true"></div>
  <div class="from-primary via-primary/95 to-primary/90 absolute inset-0 bg-gradient-to-b to-transparent" aria-hidden="true"></div>
  <div class="relative mx-auto flex w-full max-w-6xl flex-col items-center gap-6 px-4 py-20 text-center sm:px-6 sm:py-28">
    <p class="text-cta inline-flex items-center gap-2 rounded-full bg-white/10 px-3 py-1 text-[11px] font-semibold tracking-[0.18em] uppercase backdrop-blur">
      <Timer class="size-3.5" aria-hidden="true" /> Premium parking rental
    </p>
    <h1 class="text-primary-foreground max-w-3xl text-4xl font-semibold tracking-tight text-balance sm:text-6xl">
      Reserve your spot before you <span class="text-cta">drive</span>.
    </h1>
    <p class="text-primary-foreground/75 max-w-xl text-base leading-7 sm:text-lg">
      Live spot maps, hourly or weekly windows, and Spot Cover on every lot.
      No circling, no coin feeder, no guesswork.
    </p>
    <div class="mt-2 flex flex-wrap items-center justify-center gap-3">
      <Button href="/lots" size="lg" class="rounded-2xl px-7">
        Find parking <ArrowRight class="size-4" aria-hidden="true" />
      </Button>
      <a
        href="/about"
        class="text-primary-foreground/90 hover:bg-white/10 inline-flex h-11 items-center rounded-2xl px-6 text-[15px] font-medium backdrop-blur transition-colors"
      >How it works</a>
    </div>
    {#if lots.length}
      <p class="text-primary-foreground/60 mt-6 font-mono text-xs tabular-nums">
        {lots.reduce((sum, lot) => sum + lot.free, 0)} spots free right now across {lots.length} lots
      </p>
    {/if}
  </div>
</section>

<!-- Lot carousel -->
<section class="mx-auto -mt-8 w-full max-w-6xl px-4 sm:px-6">
  <div class="flex items-end justify-between gap-4">
    <div>
      <h2 class="text-2xl font-semibold tracking-tight">Live decks</h2>
      <p class="text-muted-foreground mt-1 text-[15px]">Occupancy updates in real time as cars roll in and out.</p>
    </div>
    <Link href="/lots" class="text-muted-foreground hover:text-foreground inline-flex items-center gap-1 text-sm font-medium whitespace-nowrap hover:underline underline-offset-4">
      All lots <ArrowRight class="size-4" aria-hidden="true" />
    </Link>
  </div>
  {#if lots.length}
    <div class="scrollbar-none mt-6 flex snap-x snap-mandatory gap-4 overflow-x-auto pb-4 lg:grid lg:grid-cols-3 lg:overflow-visible">
      {#each lots.slice(0, 6) as lot (lot.id)}
        <div class="w-[21rem] shrink-0 snap-start sm:w-96 lg:w-auto">
          <LotCard {lot} />
        </div>
      {/each}
    </div>
  {:else}
    <div class="mt-6 rounded-3xl border border-dashed px-6 py-16 text-center">
      <CarFront class="text-muted-foreground mx-auto size-8" strokeWidth={1.5} aria-hidden="true" />
      <p class="font-display mt-3 font-semibold">No decks are open yet</p>
      <p class="text-muted-foreground mx-auto mt-1 max-w-sm text-sm">Lots appear here the moment the team opens them for bookings.</p>
    </div>
  {/if}
</section>

<!-- How it works -->
<section class="mx-auto mt-16 w-full max-w-6xl px-4 pb-20 sm:px-6">
  <div class="bg-card shadow-card rounded-3xl border p-8 sm:p-10">
    <h2 class="text-2xl font-semibold tracking-tight">How it works</h2>
    <div class="mt-8 grid gap-8 sm:grid-cols-3">
      {#each [[MapPin, 'Pick a lot & spot', 'Browse live decks, see free bays per level, and choose the exact spot that suits you — EV, covered or accessible.'],
              [CalendarRange, 'Choose a window', 'Book a single stretch of hours or a recurring weekly loop — same days, same times, up to eight weeks out.'],
              [CreditCard, 'Pay & roll in', 'Card, mobile money or pay at gate. The guard checks your plate against the window and you roll straight in.']] as [Icon, title, body] (title)}
        <div class="flex flex-col gap-3">
          <span class="bg-cta/15 text-cta-foreground flex size-10 items-center justify-center rounded-2xl">
            <Icon class="size-5" strokeWidth={1.75} aria-hidden="true" />
          </span>
          <h3 class="text-base font-semibold">{title}</h3>
          <p class="text-muted-foreground text-sm leading-6">{body}</p>
        </div>
      {/each}
    </div>
    <div class="bg-spot-free/10 border-spot-free/30 mt-8 flex items-start gap-3 rounded-2xl border p-4">
      <ShieldCheck class="text-spot-free-foreground mt-0.5 size-5 shrink-0" aria-hidden="true" />
      <p class="text-sm leading-6">
        <span class="font-medium">Spot Cover ships with every lot</span>
        <span class="text-muted-foreground"> — 12% of parking (min $2) buys coverage up to the lot's limit for damage or theft while you're parked.</span>
      </p>
    </div>
  </div>
</section>
