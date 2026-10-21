<script lang="ts">
  import { Donut } from '@unovis/ts'
  import { onMount } from 'svelte'
  import { chartTheme } from './chart-utils'
  import { statusLabel, statusTone } from '@/lib/status'
  import type { RefundMixRow } from '@/types'

  /** Refund decision mix over the last 14 days as a donut. */
  let { rows }: { rows: RefundMixRow[] } = $props()

  let el: HTMLDivElement | undefined = $state()
  let mounted = $state(false)
  onMount(() => (mounted = true))

  const total = $derived(rows.reduce((sum, row) => sum + row.count, 0))
  const color = (row: RefundMixRow) => `var(--tone-${statusTone(row.status)})`

  // Map tones onto chart palette vars so light/dark both read well.
  const theme = [
    chartTheme,
    '--tone-mint:var(--chart-3)',
    '--tone-violet:var(--chart-4)',
    '--tone-ink:var(--chart-1)',
    '--tone-amber:var(--chart-2)',
    '--tone-red:var(--chart-5)',
    '--tone-grey:color-mix(in oklab, var(--muted-foreground) 60%, transparent)',
    '--tone-navy:var(--chart-1)',
  ].join(';')

  $effect(() => {
    if (!mounted || !el) return
    const donut = new Donut<RefundMixRow>(el, {
      value: (d: RefundMixRow) => d.count,
      color,
      angleRange: [0, 2 * Math.PI],
      padAngle: 0.02,
      arcWidth: 24,
      showBackground: false,
      centralLabel: total ? String(total) : '',
      centralSubLabel: total ? 'refunds' : '',
    }, rows)
    return () => donut.destroy()
  })
</script>

{#if !total}
  <p class="text-muted-foreground py-10 text-center text-sm">No refund decisions in the last 14 days.</p>
{:else}
  <div class="flex flex-col items-center gap-5 px-5 pb-5 sm:flex-row sm:justify-center sm:gap-10">
    <div class="relative" style={theme}>
      {#if mounted}
        <div bind:this={el} class="h-44 w-44"></div>
      {:else}
        <div class="bg-muted/40 size-44 rounded-full"></div>
      {/if}
      {#if !mounted}
        <div class="absolute inset-0 flex flex-col items-center justify-center">
          <span class="font-mono text-xl font-semibold tabular-nums">{total}</span>
          <span class="text-muted-foreground text-xs">refunds</span>
        </div>
      {/if}
    </div>
    <ul class="grid gap-2 text-sm">
      {#each rows as row (row.status)}
        <li class="flex items-center gap-2.5">
          <span class="size-2.5 rounded-xs" style={`background-color:${color(row)}`} aria-hidden="true"></span>
          <span class="text-muted-foreground">{statusLabel(row.status)}</span>
          <span class="font-mono ml-auto pl-4 tabular-nums">{row.count}</span>
        </li>
      {/each}
    </ul>
  </div>
{/if}
