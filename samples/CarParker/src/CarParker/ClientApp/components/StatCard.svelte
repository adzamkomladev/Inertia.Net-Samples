<script lang="ts">
  import type { Component, Snippet } from 'svelte'
  import { TrendingDown, TrendingUp } from '@lucide/svelte'
  import { cn } from '@/lib/utils'

  /** KPI tile: big display number, label, optional trend chip and footnote. */
  let {
    label,
    value,
    trend,
    trendLabel,
    hint,
    icon: Icon,
    class: className,
    children,
  }: {
    label: string
    value: string | number
    /** +N or -N; the chip colors from the sign. */
    trend?: number
    trendLabel?: string
    hint?: string
    icon?: Component<Record<string, any>>
    class?: string
    children?: Snippet
  } = $props()
</script>

<div class={cn('bg-card shadow-card ring-foreground/5 relative overflow-hidden rounded-2xl p-5 ring-1', className)}>
  <div class="flex items-start justify-between gap-3">
    <p class="text-muted-foreground text-[13px] font-medium">{label}</p>
    {#if Icon}
      <span class="bg-muted text-muted-foreground flex size-8 shrink-0 items-center justify-center rounded-lg" aria-hidden="true">
        <Icon class="size-4" strokeWidth={1.75} />
      </span>
    {/if}
  </div>
  <p class="font-display mt-2 text-[28px] leading-none font-semibold tracking-tight tabular-nums">{value}</p>
  <div class="mt-2.5 flex items-center gap-2 text-xs">
    {#if trend !== undefined && trend !== 0}
      <span
        class={cn(
          'inline-flex items-center gap-1 rounded-full px-1.5 py-0.5 font-medium',
          trend > 0 ? 'bg-spot-free/15 text-spot-free-foreground' : 'bg-destructive/10 text-destructive',
        )}
      >
        {#if trend > 0}<TrendingUp class="size-3" aria-hidden="true" />{:else}<TrendingDown class="size-3" aria-hidden="true" />{/if}
        {Math.abs(trend)}%
      </span>
    {/if}
    {#if trendLabel || hint}<span class="text-muted-foreground truncate">{trendLabel ?? hint}</span>{/if}
  </div>
  {@render children?.()}
</div>
