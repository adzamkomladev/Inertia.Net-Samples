<script lang="ts">
  import { moneyCompact } from '@/lib/money'
  import type { TopLotRow } from '@/types'

  /** Lots ranked by revenue, with sessions and occupancy beside each bar. */
  let { lots }: { lots: TopLotRow[] } = $props()

  const max = $derived(Math.max(1, ...lots.map(l => l.revenueCents)))
</script>

{#if !lots.length}
  <p class="text-muted-foreground py-10 text-center text-sm">No revenue in this window yet.</p>
{:else}
  <ol class="grid gap-4">
    {#each lots as lot (lot.id)}
      <li>
        <div class="flex items-baseline justify-between gap-3">
          <p class="truncate font-semibold">{lot.name}</p>
          <p class="font-display text-lg font-bold tabular-nums">{moneyCompact(lot.revenueCents)}</p>
        </div>
        <div class="bg-muted mt-1.5 h-2 overflow-hidden rounded-full" aria-hidden="true">
          <div class="bg-primary h-full rounded-full transition-[width] duration-500" style={`width:${(lot.revenueCents / max) * 100}%`}></div>
        </div>
        <p class="text-muted-foreground mt-1 text-sm tabular-nums">{lot.sessions} sessions · {lot.occupancyPct}% occupied</p>
      </li>
    {/each}
  </ol>
{/if}
