<script lang="ts">
  import type { Component, Snippet } from 'svelte'
  import { TrendingDown, TrendingUp } from '@lucide/svelte'
  import { cn } from '@/lib/utils'

  /** KPI tile: label, one big figure, optional trend and footnote. */
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
    /** +N or -N; the trend colours from the sign. */
    trend?: number
    trendLabel?: string
    hint?: string
    icon?: Component<Record<string, any>>
    class?: string
    children?: Snippet
  } = $props()
</script>

<div class={cn('bg-card shadow-card rounded-xl p-4 sm:p-5', className)}>
  <div class="text-muted-foreground flex items-center gap-2 text-sm font-medium">
    {#if Icon}<Icon class="size-4 shrink-0" strokeWidth={1.75} aria-hidden="true" />{/if}
    <p class="truncate">{label}</p>
  </div>
  <p class="font-display mt-2 text-3xl leading-none font-bold tabular-nums">{value}</p>
  {#if trend || trendLabel || hint}
    <div class="mt-2.5 flex items-center gap-2 text-sm">
      {#if trend !== undefined && trend !== 0}
        <span class={cn('inline-flex items-center gap-1 font-semibold', trend > 0 ? 'text-spot-free-foreground' : 'text-destructive')}>
          {#if trend > 0}<TrendingUp class="size-3.5" aria-hidden="true" />{:else}<TrendingDown class="size-3.5" aria-hidden="true" />{/if}
          {Math.abs(trend)}%
        </span>
      {/if}
      {#if trendLabel || hint}<span class="text-muted-foreground">{trendLabel ?? hint}</span>{/if}
    </div>
  {/if}
  {@render children?.()}
</div>
