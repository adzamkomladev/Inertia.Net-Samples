<script lang="ts">
  import { CarFront, Clock3, MapPin, ShieldCheck, Zap } from '@lucide/svelte'
  import OccupancyRing from '@/components/OccupancyRing.svelte'
  import PriceTag from '@/components/PriceTag.svelte'
  import { money } from '@/lib/money'
  import type { LotCard as LotCardType } from '@/types'

  /** Lot summary card for the home carousel and the /lots grid. */
  let { lot, href }: { lot: LotCardType; href?: string } = $props()

  const typeLabels: Record<string, string> = {
    ev: 'EV charging',
    handicap: 'Accessible',
    covered: 'Covered',
    compact: 'Compact',
    standard: 'Standard',
  }
</script>

<a
  href={href ?? `/lots/${lot.id}`}
  class="group bg-card shadow-card ring-foreground/5 relative flex flex-col overflow-hidden rounded-3xl ring-1 transition-all duration-300 ease-spring hover:-translate-y-0.5 hover:shadow-lifted"
>
  <div class="deck-grid bg-primary relative overflow-hidden px-5 pt-5 pb-4 text-primary-foreground">
    <div class="flex items-start justify-between gap-3">
      <div class="min-w-0">
        <p class="text-primary-foreground/60 text-[11px] font-medium tracking-[0.14em] uppercase">{lot.area}</p>
        <h3 class="mt-0.5 truncate text-lg font-semibold">{lot.name}</h3>
        <p class="text-primary-foreground/70 mt-1 flex items-center gap-1.5 text-[13px]">
          <MapPin class="size-3.5 shrink-0" aria-hidden="true" />
          <span class="truncate">{lot.address}</span>
        </p>
      </div>
      <div class="shrink-0">
        <OccupancyRing value={lot.occupancyPct} size={64} label="occupancy" class="text-primary-foreground" />
      </div>
    </div>
  </div>

  <div class="flex flex-1 flex-col gap-4 p-5">
    <div class="grid grid-cols-2 gap-3">
      <div class="rounded-xl border p-3">
        <p class="text-muted-foreground text-[11px] font-medium tracking-wide uppercase">Per hour</p>
        <PriceTag cents={lot.ratePerHourCents} size="lg" class="mt-1" />
      </div>
      <div class="rounded-xl border p-3">
        <p class="text-muted-foreground text-[11px] font-medium tracking-wide uppercase">Daily max</p>
        <PriceTag cents={lot.dailyMaxCents} size="lg" class="mt-1" />
      </div>
    </div>

    <div class="flex flex-wrap items-center gap-x-3 gap-y-1.5 text-[13px]">
      <span class="inline-flex items-center gap-1.5 font-medium">
        <span class="bg-spot-free size-2 rounded-full" aria-hidden="true"></span>
        <span class="font-mono tabular-nums">{lot.free}</span> of <span class="font-mono tabular-nums">{lot.spots}</span> free
      </span>
      <span class="text-muted-foreground inline-flex items-center gap-1">
        <CarFront class="size-3.5" aria-hidden="true" /> {lot.levelCount} level{lot.levelCount === 1 ? '' : 's'}
      </span>
      {#if lot.insuranceCovered}
        <span class="text-muted-foreground inline-flex items-center gap-1" title={`Spot Cover up to ${money(lot.coverageCents)}`}>
          <ShieldCheck class="size-3.5" aria-hidden="true" /> Spot Cover
        </span>
      {/if}
    </div>

    {#if lot.spotTypes.length}
      <div class="mt-auto flex flex-wrap gap-1.5">
        {#each lot.spotTypes.slice(0, 4) as type (type)}
          <span class="bg-muted text-muted-foreground inline-flex items-center gap-1 rounded-full px-2 py-0.5 text-[11px]">
            {#if type === 'ev'}<Zap class="size-3" aria-hidden="true" />{:else if type === 'covered'}<Clock3 class="size-3" aria-hidden="true" />{/if}
            {typeLabels[type] ?? type}
          </span>
        {/each}
      </div>
    {/if}
  </div>
</a>
