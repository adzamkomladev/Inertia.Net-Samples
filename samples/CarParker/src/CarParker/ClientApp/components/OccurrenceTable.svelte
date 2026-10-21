<script lang="ts">
  import StatusBadge from '@/components/StatusBadge.svelte'
  import { fmtDate, fmtTime } from '@/lib/time'
  import type { OccurrenceView } from '@/types'

  /** The materialized windows of a recurring (or single) booking, newest index first. */
  let {
    occurrences,
    class: className,
  }: {
    occurrences: OccurrenceView[]
    class?: string
  } = $props()
</script>

{#if !occurrences.length}
  <p class="text-muted-foreground py-8 text-center text-sm">No windows on this booking.</p>
{:else}
  <div class={className}>
    <!-- Desktop -->
    <table class="hidden w-full border-collapse text-sm md:table">
      <thead>
        <tr class="border-b">
          <th scope="col" class="text-muted-foreground px-4 py-2 text-left text-xs font-medium">#</th>
          <th scope="col" class="text-muted-foreground px-4 py-2 text-left text-xs font-medium">Date</th>
          <th scope="col" class="text-muted-foreground px-4 py-2 text-left text-xs font-medium">Window · UTC</th>
          <th scope="col" class="text-muted-foreground px-4 py-2 text-right text-xs font-medium">Status</th>
        </tr>
      </thead>
      <tbody>
        {#each occurrences as occurrence (occurrence.id)}
          <tr class="hover:bg-accent/40 border-b transition-colors last:border-b-0">
            <td class="px-4 py-2.5 font-mono text-xs tabular-nums">{occurrence.index + 1}</td>
            <td class="px-4 py-2.5">{fmtDate(occurrence.startAtUtc, true)}</td>
            <td class="px-4 py-2.5 font-mono text-[13px] tabular-nums">{fmtTime(occurrence.startAtUtc)}–{fmtTime(occurrence.endAtUtc)}</td>
            <td class="px-4 py-2.5 text-right"><StatusBadge status={occurrence.status} size="xs" /></td>
          </tr>
        {/each}
      </tbody>
    </table>
    <!-- Mobile -->
    <ul class="grid gap-2 md:hidden">
      {#each occurrences as occurrence (occurrence.id)}
        <li class="bg-muted/40 flex items-center justify-between gap-3 rounded-xl px-3 py-2.5">
          <div class="min-w-0">
            <p class="text-[13px] font-medium">{fmtDate(occurrence.startAtUtc, true)}</p>
            <p class="text-muted-foreground font-mono text-xs tabular-nums">{fmtTime(occurrence.startAtUtc)}–{fmtTime(occurrence.endAtUtc)}</p>
          </div>
          <StatusBadge status={occurrence.status} size="xs" />
        </li>
      {/each}
    </ul>
  </div>
{/if}
