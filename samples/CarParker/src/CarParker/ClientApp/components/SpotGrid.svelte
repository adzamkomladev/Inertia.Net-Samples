<script lang="ts">
  import type { Component } from 'svelte'
  import { BatteryCharging, CarFront, CircleAlert, Accessibility, Zap, Square } from '@lucide/svelte'
  import { cn } from '@/lib/utils'
  import type { SpotView } from '@/types'

  /**
   * Live spot map: bays grouped by level, tiles colored by the deck's spot-state code
   * (mint free / violet booked / ink occupied / grey stripes blocked), type icons, hover
   * detail and optimistic SignalR flips applied to the `spots` prop by useRealtime.
   */
  let {
    spots,
    selectable = false,
    selectedId,
    compact = false,
    class: className,
    onselect,
  }: {
    spots: SpotView[]
    selectable?: boolean
    selectedId?: string | null
    /** Smaller tiles for guard/admin consoles with more spots. */
    compact?: boolean
    class?: string
    onselect?: (spot: SpotView) => void
  } = $props()

  type State = 'free' | 'booked' | 'occupied' | 'blocked'

  const stateMeta: Record<State, { label: string; tile: string }> = {
    free: { label: 'Free', tile: 'bg-spot-free text-spot-free-foreground border-spot-free' },
    booked: { label: 'Booked', tile: 'bg-spot-booked text-spot-booked-foreground border-spot-booked' },
    occupied: { label: 'Occupied', tile: 'bg-spot-occupied text-spot-occupied-foreground border-spot-occupied' },
    blocked: { label: 'Blocked', tile: 'stripes-blocked bg-spot-blocked text-spot-blocked-foreground border-spot-blocked' },
  }

  const typeIcon: Record<string, Component<Record<string, any>>> = {
    ev: Zap,
    handicap: Accessibility,
    covered: Square,
    compact: CarFront,
  }

  const levels = $derived.by(() => {
    const map = new Map<number, SpotView[]>()
    for (const spot of spots) {
      const row = map.get(spot.level) ?? []
      row.push(spot)
      map.set(spot.level, row)
    }
    return [...map.entries()].sort((a, b) => a[0] - b[0])
  })

  const counts = $derived.by(() => {
    const tally: Record<State, number> = { free: 0, booked: 0, occupied: 0, blocked: 0 }
    for (const spot of spots) tally[(spot.state as State) in tally ? (spot.state as State) : 'free']++
    return tally
  })

  const shortLabel = (label: string) => (compact ? label.replace(/^[A-Z]-/, '') : label)
</script>

<div class={cn('flex flex-col gap-5', className)}>
  {#each levels as [level, levelSpots] (level)}
    <section>
      <div class="mb-2 flex items-center gap-3">
        <h3 class="text-muted-foreground font-display text-[11px] font-semibold tracking-[0.16em] uppercase">Level {level}</h3>
        <span class="bg-border h-px flex-1" aria-hidden="true"></span>
        <span class="text-muted-foreground font-mono text-[11px] tabular-nums">
          {levelSpots.filter(s => s.state === 'free').length}/{levelSpots.length} free
        </span>
      </div>
      <div class="grid grid-cols-4 gap-1.5 sm:grid-cols-6 sm:gap-2 md:grid-cols-8 lg:grid-cols-10 xl:grid-cols-12">
        {#each levelSpots as spot (spot.id)}
          {@const meta = stateMeta[(spot.state as State) in stateMeta ? (spot.state as State) : 'free']}
          {@const TypeIcon = typeIcon[spot.type]}
          {@const selected = selectedId === spot.id}
          <button
            type="button"
            disabled={!selectable || spot.state !== 'free'}
            aria-pressed={selected}
            aria-label={`Spot ${spot.label} — ${meta.label}${spot.type === 'standard' ? '' : `, ${spot.type}`}`}
            onclick={() => onselect?.(spot)}
            class={cn(
              'group/tile relative flex aspect-[4/3.4] flex-col items-center justify-center gap-0.5 rounded-lg border transition-all duration-200 ease-spring',
              meta.tile,
              !selectable && 'cursor-default',
              selectable && spot.state === 'free' && 'hover:-translate-y-0.5 hover:shadow-pop cursor-pointer focus-visible:-translate-y-0.5',
              selectable && 'aria-[pressed=true]:ring-2 aria-[pressed=true]:ring-foreground aria-[pressed=true]:ring-offset-2 aria-[pressed=true]:ring-offset-background',
              selected && 'ring-2 ring-foreground ring-offset-2 ring-offset-background',
            )}
          >
            <span class="font-mono text-[11px] leading-none font-semibold sm:text-xs">{shortLabel(spot.label)}</span>
            <span class="flex items-center gap-0.5" aria-hidden="true">
              {#if spot.type !== 'standard' && TypeIcon}
                <TypeIcon class="size-3 opacity-80" strokeWidth={2.25} />
              {/if}
            </span>
            {#if spot.note && spot.state === 'blocked'}
              <CircleAlert class="absolute top-1 right-1 size-3 opacity-70" aria-hidden="true" />
            {/if}
            <!-- Hover detail -->
            <span
              class="bg-popover text-popover-foreground pointer-events-none absolute -top-1 left-1/2 z-20 hidden w-max -translate-x-1/2 -translate-y-full rounded-lg border px-2.5 py-1.5 text-left text-[11px] leading-snug shadow-pop group-hover/tile:block"
              role="tooltip"
            >
              <span class="font-semibold">{spot.label}</span> · {meta.label}
              {#if spot.type !== 'standard'}<span class="text-muted-foreground block capitalize">{spot.type}</span>{/if}
              {#if spot.note}<span class="text-muted-foreground block italic">{spot.note}</span>{/if}
            </span>
          </button>
        {/each}
      </div>
    </section>
  {/each}

  <!-- Legend -->
  <footer class="text-muted-foreground flex flex-wrap items-center gap-x-4 gap-y-1.5 text-[11px]">
    {#each Object.entries(stateMeta) as [state, meta] (state)}
      <span class="inline-flex items-center gap-1.5">
        <span class={cn('size-3 rounded-[4px] border', meta.tile)} aria-hidden="true"></span>
        {meta.label}
        <span class="font-mono tabular-nums">{counts[state as State]}</span>
      </span>
    {/each}
    <span class="ml-auto inline-flex items-center gap-1">
      <BatteryCharging class="size-3" aria-hidden="true" /> EV · <Accessibility class="ml-1 size-3" aria-hidden="true" /> Accessible ·
      <Square class="ml-1 size-3" aria-hidden="true" /> Covered · <CarFront class="ml-1 size-3" aria-hidden="true" /> Compact
    </span>
  </footer>
</div>
