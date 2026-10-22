<script lang="ts">
  import type { Component } from 'svelte'
  import { Accessibility, Ban, Check, CarFront, Clock3, Umbrella, Zap } from '@lucide/svelte'
  import { cn } from '@/lib/utils'
  import type { SpotView } from '@/types'

  /**
   * Floor-plan spot map. A level tab picks the floor; each section is drawn as two facing
   * rows of numbered bays with a driving aisle between them. State is never colour-only:
   * every bay carries a glyph (check, clock, car, ban) and an aria-label. Bays are at least
   * 44px wide and tall, focusable, and flip live through the `spots` prop.
   */
  let {
    spots,
    selectable = false,
    selectableStates = ['free'],
    selectedId,
    legend = true,
    class: className,
    onselect,
  }: {
    spots: SpotView[]
    selectable?: boolean
    /** Which bay states respond to a click when `selectable`. */
    selectableStates?: string[]
    selectedId?: string | null
    legend?: boolean
    class?: string
    onselect?: (spot: SpotView) => void
  } = $props()

  type State = 'free' | 'booked' | 'occupied' | 'blocked'

  const stateMeta: Record<State, { label: string; bay: string; glyph: Component<Record<string, any>> | null }> = {
    free: { label: 'Free', bay: 'bg-spot-free/25 border-spot-free text-foreground', glyph: null },
    booked: { label: 'Booked', bay: 'bg-spot-booked/12 border-spot-booked/70 text-spot-booked', glyph: Clock3 },
    occupied: { label: 'Occupied', bay: 'bg-spot-occupied border-spot-occupied text-spot-occupied-foreground', glyph: CarFront },
    blocked: { label: 'Blocked', bay: 'stripes-blocked bg-spot-blocked border-spot-blocked text-spot-blocked-foreground', glyph: Ban },
  }

  const typeMeta: Record<string, { label: string; icon: Component<Record<string, any>> }> = {
    ev: { label: 'EV charging', icon: Zap },
    handicap: { label: 'Accessible', icon: Accessibility },
    covered: { label: 'Covered', icon: Umbrella },
    compact: { label: 'Compact', icon: CarFront },
  }

  const stateOf = (spot: SpotView): State => (spot.state in stateMeta ? (spot.state as State) : 'free')
  const sectionOf = (spot: SpotView) => spot.label.match(/^([A-Za-z]+)-/)?.[1]?.toUpperCase() ?? ''
  const numberOf = (spot: SpotView) => spot.label.replace(/^[A-Za-z]+-/, '')

  const levels = $derived.by(() => {
    const map = new Map<number, Map<string, SpotView[]>>()
    for (const spot of spots) {
      const sections = map.get(spot.level) ?? new Map<string, SpotView[]>()
      const row = sections.get(sectionOf(spot)) ?? []
      row.push(spot)
      sections.set(sectionOf(spot), row)
      map.set(spot.level, sections)
    }
    return [...map.entries()]
      .sort((a, b) => a[0] - b[0])
      .map(([level, sections]) => {
        const all = [...sections.values()].flat()
        return {
          level,
          free: all.filter(s => s.state === 'free').length,
          sections: [...sections.entries()].map(([name, list]) => {
            const sorted = [...list].sort((a, b) => a.label.localeCompare(b.label, undefined, { numeric: true }))
            const half = Math.ceil(sorted.length / 2)
            return { name, rows: [sorted.slice(0, half), sorted.slice(half)] as SpotView[][] }
          }),
        }
      })
  })

  const counts = $derived.by(() => {
    const tally: Record<State, number> = { free: 0, booked: 0, occupied: 0, blocked: 0 }
    for (const spot of spots) tally[stateOf(spot)]++
    return tally
  })

  const presentTypes = $derived(Object.keys(typeMeta).filter(type => spots.some(s => s.type === type)))

  let picked = $state<number | null>(null)
  const selectedLevel = $derived(spots.find(s => s.id === selectedId)?.level)
  const activeLevel = $derived(
    picked !== null && levels.some(l => l.level === picked)
      ? picked
      : (selectedLevel ?? levels.find(l => l.free > 0)?.level ?? levels[0]?.level ?? 0),
  )
  const active = $derived(levels.find(l => l.level === activeLevel))

  const tabKeys = (event: KeyboardEvent) => {
    const step = event.key === 'ArrowRight' ? 1 : event.key === 'ArrowLeft' ? -1 : 0
    if (!step) return
    const index = levels.findIndex(l => l.level === activeLevel)
    const next = levels[(index + step + levels.length) % levels.length]
    if (next) picked = next.level
    event.preventDefault()
  }
</script>

<div class={cn('flex flex-col gap-4', className)}>
  {#if levels.length > 1}
    <div class="bg-muted flex w-fit max-w-full gap-1 overflow-x-auto rounded-xl p-1" role="tablist" aria-label="Level">
      {#each levels as item (item.level)}
        <button
          type="button"
          role="tab"
          aria-selected={item.level === activeLevel}
          tabindex={item.level === activeLevel ? 0 : -1}
          onclick={() => (picked = item.level)}
          onkeydown={tabKeys}
          class={cn(
            'flex min-h-11 shrink-0 flex-col items-start rounded-lg px-4 py-1.5 text-left transition-colors md:min-h-10',
            item.level === activeLevel ? 'bg-card shadow-card' : 'text-muted-foreground hover:text-foreground',
          )}
        >
          <span class="font-display text-sm leading-tight font-semibold">Level {item.level}</span>
          <span class="text-xs leading-tight tabular-nums">{item.free} free</span>
        </button>
      {/each}
    </div>
  {/if}

  {#if active}
    <div class="bg-muted/70 flex flex-col gap-5 rounded-xl p-3 sm:p-5" role="tabpanel" aria-label={`Level ${active.level} floor plan`}>
      {#each active.sections as section (section.name)}
        <section aria-label={section.name ? `Section ${section.name}` : 'Bays'}>
          {#if section.name}
            <h3 class="mb-2 flex items-center gap-2 text-xs font-semibold tracking-wide uppercase">
              <span class="bg-foreground text-background grid size-6 place-items-center rounded-md text-sm">{section.name}</span>
              <span class="text-muted-foreground">Section {section.name}</span>
            </h3>
          {/if}
          {#each section.rows as row, rowIndex (rowIndex)}
            {#if rowIndex === 1 && row.length}
              <!-- Driving aisle between the facing rows -->
              <div class="text-muted-foreground/70 my-1.5 flex items-center gap-2 text-[10px] font-semibold tracking-[0.2em] uppercase" aria-hidden="true">
                <span class="border-muted-foreground/30 h-0 flex-1 border-t-2 border-dashed"></span>
                Aisle
                <span class="border-muted-foreground/30 h-0 flex-1 border-t-2 border-dashed"></span>
              </div>
            {/if}
            {#if row.length}
              <div class="grid grid-cols-6 gap-1 sm:gap-1.5" style={`max-width:${Math.max(row.length, 6) * 5.2}rem`}>
                {#each row as spot (spot.id)}
                  {@const state = stateOf(spot)}
                  {@const meta = stateMeta[state]}
                  {@const type = typeMeta[spot.type]}
                  {@const selected = selectedId === spot.id}
                  {@const Glyph = selected ? Check : meta.glyph}
                  {@const clickable = selectable && selectableStates.includes(spot.state)}
                  <button
                    type="button"
                    disabled={!clickable}
                    aria-pressed={selectable ? selected : undefined}
                    aria-label={`Spot ${spot.label}, ${meta.label}${type ? `, ${type.label}` : ''}${spot.note ? `, ${spot.note}` : ''}`}
                    title={`${spot.label} · ${meta.label}${type ? ` · ${type.label}` : ''}`}
                    onclick={() => onselect?.(spot)}
                    class={cn(
                      'relative flex h-16 min-w-0 flex-col items-center justify-center gap-0.5 rounded-md border-2 text-xs font-semibold transition-colors sm:h-[4.5rem]',
                      selected ? 'bg-primary text-primary-foreground border-primary' : meta.bay,
                      clickable && !selected && 'hover:bg-spot-free/50 cursor-pointer',
                      !clickable && 'cursor-default',
                      'focus-visible:ring-ring focus-visible:ring-offset-muted focus-visible:ring-2 focus-visible:ring-offset-2 focus-visible:outline-none',
                    )}
                  >
                    {#if type}
                      <type.icon class="absolute top-1 right-1 size-3 opacity-70" aria-hidden="true" />
                    {/if}
                    <span class="font-display text-base leading-none tabular-nums">{numberOf(spot)}</span>
                    {#if Glyph}<Glyph class="size-3.5" strokeWidth={2.25} aria-hidden="true" />{:else}<span class="size-3.5"></span>{/if}
                  </button>
                {/each}
              </div>
            {/if}
          {/each}
        </section>
      {/each}
    </div>
  {/if}

  {#if legend}
    <ul class="text-muted-foreground flex flex-wrap items-center gap-x-5 gap-y-2 text-sm" aria-label="Legend">
      {#each Object.entries(stateMeta) as [state, meta] (state)}
        <li class="inline-flex items-center gap-2">
          <span class={cn('grid size-6 place-items-center rounded-[5px] border-2', meta.bay)} aria-hidden="true">
            {#if meta.glyph}<meta.glyph class="size-3" strokeWidth={2.25} />{/if}
          </span>
          {meta.label} <span class="text-foreground font-semibold tabular-nums">{counts[state as State]}</span>
        </li>
      {/each}
      {#each presentTypes as type (type)}
        {@const entry = typeMeta[type]!}
        <li class="inline-flex items-center gap-1.5"><entry.icon class="size-4" aria-hidden="true" /> {entry.label}</li>
      {/each}
    </ul>
  {/if}
</div>
