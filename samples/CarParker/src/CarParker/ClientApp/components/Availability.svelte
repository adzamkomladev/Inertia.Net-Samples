<script lang="ts">
  import { cn } from '@/lib/utils'

  /** Live availability: a bar plus words, so the state never relies on colour alone. */
  let {
    free,
    total,
    size = 'md',
    class: className,
  }: {
    free: number
    total: number
    size?: 'sm' | 'md'
    class?: string
  } = $props()

  const ratio = $derived(total ? free / total : 0)
  const level = $derived(free === 0 ? 'full' : ratio < 0.15 ? 'low' : ratio < 0.4 ? 'busy' : 'open')
  const words = { full: 'Full', low: 'Almost full', busy: 'Filling up', open: 'Plenty of space' }
  const bar = { full: 'bg-destructive', low: 'bg-destructive', busy: 'bg-cta', open: 'bg-spot-free' }
</script>

<div class={className}>
  <div class="flex items-baseline justify-between gap-3">
    <p class={cn('font-display font-semibold tabular-nums', size === 'md' ? 'text-lg' : 'text-base')}>
      {free}<span class="text-muted-foreground ml-1 text-sm font-medium">of {total} free</span>
    </p>
    <p class="text-muted-foreground text-sm">{words[level]}</p>
  </div>
  <div class="bg-foreground/10 mt-1.5 h-2 overflow-hidden rounded-full" role="img" aria-label={`${free} of ${total} spots free`}>
    <div class={cn('h-full rounded-full transition-[width] duration-500', bar[level])} style={`width:${Math.max(free ? 4 : 0, Math.round(ratio * 100))}%`}></div>
  </div>
</div>
