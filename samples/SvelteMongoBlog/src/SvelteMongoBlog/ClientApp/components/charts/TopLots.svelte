<script lang="ts">
  import { moneyCompact } from '@/lib/money'
  import type { TopLotRow } from '@/types'

  /** Bar list of the best-performing lots over the trailing window. */
  let { lots }: { lots: TopLotRow[] } = $props()

  const max = $derived(Math.max(1, ...lots.map(l => l.revenueCents)))
</script>

{#if !lots.length}
  <p class="text-muted-foreground py-10 text-center text-sm">No revenue in this window yet.</p>
{:else}
  <ol class="grid gap-3 px-5 pb-5">
    {#each lots as lot, i (lot.id)}
      <li class="grid grid-cols-[2rem_1fr] items-center gap-x-3 sm:grid-cols-[2rem_1fr_auto]">
        <span class="text-muted-foreground font-mono text-xs tabular-nums">{i + 1}</span>
        <div class="min-w-0">
          <div class="flex items-baseline justify-between gap-3">
            <p class="truncate text-sm font-medium">{lot.name}</p>
            <p class="font-mono text-[13px] tabular-nums">{moneyCompact(lot.revenueCents)}</p>
          </div>
          <div class="bg-muted mt-1.5 flex items-center gap-2">
            <span class="bg-primary relative h-1.5 flex-1 overflow-hidden rounded-full" aria-hidden="true">
              <span class="bg-cta absolute inset-y-0 left-0 rounded-full transition-all duration-500 ease-glide" style={`width:${(lot.revenueCents / max) * 100}%`}></span>
            </span>
            <span class="text-muted-foreground w-28 text-right font-mono text-[11px] tabular-nums">
              {lot.sessions} sess · {lot.occupancyPct}% occ
            </span>
          </div>
        </div>
      </li>
    {/each}
  </ol>
{/if}
