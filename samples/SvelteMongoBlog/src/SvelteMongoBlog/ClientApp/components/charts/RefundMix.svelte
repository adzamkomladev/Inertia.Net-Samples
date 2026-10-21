<script lang="ts">
  import { statusLabel } from '@/lib/status'
  import type { RefundMixRow } from '@/types'

  /** Horizontal bar breakdown of refund decisions — pure CSS, themable. */
  let { rows }: { rows: RefundMixRow[] } = $props()

  const total = $derived(rows.reduce((sum, row) => sum + row.count, 0))
  const max = $derived(Math.max(1, ...rows.map((row) => row.count)))
</script>

<div class="flex flex-col gap-3">
  {#if total > 0}
    {#each rows as row (row.status)}
      <div class="flex items-center gap-3">
        <span class="w-24 shrink-0 text-sm">{statusLabel(row.status)}</span>
        <div class="bg-muted/60 h-2 flex-1 overflow-hidden rounded-full">
          <div class="bg-primary h-full rounded-full transition-[width] duration-500" style={`width:${Math.round((row.count / max) * 100)}%`}></div>
        </div>
        <span class="text-muted-foreground w-12 shrink-0 text-right text-sm tabular-nums">{row.count}</span>
      </div>
    {/each}
  {:else}
    <p class="text-muted-foreground py-6 text-center text-sm">No refunds recorded in this window.</p>
  {/if}
</div>
