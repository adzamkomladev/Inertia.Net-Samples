<script lang="ts">
  import { cn } from '@/lib/utils'

  /** Radial gauge for lot occupancy: 0–100%, turning amber/red as the deck fills. */
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

  const pct = $derived(Math.max(0, Math.min(100, value)))
  // 270° sweep starting bottom-left (gap at the bottom), like a speedometer.
  const angle = $derived(pct * 2.7)
  const color = $derived(pct >= 90 ? 'var(--destructive)' : pct >= 70 ? 'var(--cta)' : 'var(--spot-free)')
</script>

<div
  class={cn('relative inline-flex select-none items-center justify-center', className)}
  style={`width:${size}px;height:${size}px`}
  role="img"
  aria-label={`${label ?? 'Occupancy'}: ${Math.round(pct)}%`}
>
  <!-- track -->
  <div
    class="absolute inset-0 rounded-full"
    style={`background:conic-gradient(from 135deg, color-mix(in oklab, var(--foreground) 10%, transparent) 0deg 270deg, transparent 270deg 360deg)`}
  ></div>
  <!-- value arc -->
  <div
    class="absolute inset-0 rounded-full transition-[background] duration-500"
    style={`background:conic-gradient(from 135deg, ${color} 0deg ${angle}deg, transparent ${angle}deg 360deg)`}
  ></div>
  <!-- face -->
  <div class="bg-card absolute inset-[7px] flex flex-col items-center justify-center rounded-full">
    <span class="font-mono leading-none font-semibold tabular-nums" style={`font-size:${Math.max(11, Math.round(size / 5.4))}px`}>{Math.round(pct)}%</span>
    {#if label && size >= 80}<span class="text-muted-foreground mt-0.5 text-center leading-tight" style={`font-size:${Math.max(8, Math.round(size / 10))}px`}>{label}</span>{/if}
  </div>
</div>
