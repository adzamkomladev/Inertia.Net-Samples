<script lang="ts">
  import { Link } from '@inertiajs/svelte'
  import Accessibility from '@lucide/svelte/icons/accessibility'
  import CarFront from '@lucide/svelte/icons/car-front'
  import MapPin from '@lucide/svelte/icons/map-pin'
  import ShieldCheck from '@lucide/svelte/icons/shield-check'
  import Umbrella from '@lucide/svelte/icons/umbrella'
  import Zap from '@lucide/svelte/icons/zap'
  import type { Component } from 'svelte'
  import Availability from '@/components/Availability.svelte'
  import { money } from '@/lib/money'
  import type { LotCard as LotCardType } from '@/types'

  /** Lot row for the home page and the /lots grid: location first, then price, then live availability. */
  let { lot, href }: { lot: LotCardType; href?: string } = $props()

  const types: Record<string, { label: string; icon: Component<Record<string, any>> }> = {
    ev: { label: 'EV charging', icon: Zap },
    handicap: { label: 'Accessible', icon: Accessibility },
    covered: { label: 'Covered', icon: Umbrella },
    compact: { label: 'Compact', icon: CarFront },
  }
</script>

<Link
  href={href ?? `/lots/${lot.id}`}
  prefetch="hover"
  class="group bg-card hover:shadow-lifted focus-visible:ring-ring flex h-full flex-col gap-5 rounded-2xl p-5 shadow-card ring-1 ring-foreground/5 lift hover:ring-primary/20 focus-visible:ring-2 focus-visible:outline-none"
>
  <div class="flex items-start justify-between gap-4">
    <div class="min-w-0">
      <p class="text-muted-foreground text-xs font-semibold tracking-wider uppercase">{lot.area}</p>
      <h3 class="mt-0.5 text-xl leading-tight font-bold group-hover:underline group-hover:underline-offset-4">{lot.name}</h3>
      <p class="text-muted-foreground mt-1.5 flex items-start gap-1.5 text-sm">
        <MapPin class="mt-0.5 size-4 shrink-0" aria-hidden="true" /> {lot.address}
      </p>
    </div>
    <div class="shrink-0 text-right">
      <p class="font-display text-3xl leading-none font-bold tabular-nums">{money(lot.ratePerHourCents)}</p>
      <p class="text-muted-foreground mt-1 text-xs">per hour</p>
      <p class="text-muted-foreground text-xs tabular-nums">max {money(lot.dailyMaxCents)}/day</p>
    </div>
  </div>

  <Availability free={lot.free} total={lot.spots} />

  <div class="text-muted-foreground mt-auto flex flex-wrap items-center gap-x-4 gap-y-1.5 text-sm">
    {#each lot.spotTypes.filter(t => types[t]) as type (type)}
      {@const entry = types[type]!}
      <span class="inline-flex items-center gap-1.5"><entry.icon class="size-4" aria-hidden="true" /> {entry.label}</span>
    {/each}
    {#if lot.insuranceCovered}
      <span class="inline-flex items-center gap-1.5" title={`Spot Cover up to ${money(lot.coverageCents)}`}>
        <ShieldCheck class="size-4" aria-hidden="true" /> Spot Cover
      </span>
    {/if}
  </div>
</Link>
