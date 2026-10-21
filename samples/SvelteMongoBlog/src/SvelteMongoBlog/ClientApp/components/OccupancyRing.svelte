<script lang="ts">
  import { RadialBar } from '@unovis/ts'
  import { onMount } from 'svelte'
  import { cn } from '@/lib/utils'

  /** Unovis radial gauge for lot occupancy: 0–100%, turning amber/red as the deck fills. */
  let {
    value,
    size = 88,
    label,
    class: className,
  }: {
    value: number
    size?: number
    label?: string
    class?: string
  } = $props()

  let el: HTMLDivElement | undefined = $state()
  let mounted = $state(false)
  onMount(() => (mounted = true))

  const pct = $derived(Math.max(0, Math.min(100, value)))
  // Gauge arc: 270° sweep starting bottom-left, gap at the bottom.
  const angleRange = [(Math.PI * 3) / 4, (Math.PI * 9) / 4] as [number, number]

  const color = $derived(pct >= 90 ? 'var(--destructive)' : pct >= 70 ? 'var(--cta)' : 'var(--spot-free)')

  $effect(() => {
    if (!mounted || !el) return
    const container = new RadialBar<{ value: number }>(
      el,
      {
        value: (d: { value: number }) => d.value,
        maxValue: 100,
        angleRange,
        backgroundAngleRange: angleRange,
        trackWidth: Math.max(6, Math.round(size / 9)),
        cornerRadius: 999,
        color,
        showBackground: true,
        padAngle: 0,
        barMinAngle: 0,
      },
      [{ value: pct }],
    )
    return () => container.destroy()
  })
</script>

<div
  class={cn('relative inline-flex select-none items-center justify-center', class)}
  style={`width:${size}px;height:${size}px;--vis-radial-bar-background-color:color-mix(in oklab, var(--foreground) 8%, transparent);--vis-dark-radial-bar-background-color:color-mix(in oklab, var(--foreground) 8%, transparent);font-family:var(--font-mono)`}
  role="img"
  aria-label={`${label ?? 'Occupancy'}: ${Math.round(pct)}%`}
>
  <div bind:this={el} class="absolute inset-0"></div>
  <div class="pointer-events-none absolute inset-0 flex flex-col items-center justify-center" style={`padding-bottom:${size * 0.06}px`}>
    <span class="leading-none font-semibold tabular-nums" style={`font-size:${Math.max(11, Math.round(size / 5.4))}px`}>{Math.round(pct)}%</span>
    {#if label}<span class="text-muted-foreground mt-0.5 font-sans" style={`font-size:${Math.max(8, Math.round(size / 10))}px`}>{label}</span>{/if}
  </div>
</div>
