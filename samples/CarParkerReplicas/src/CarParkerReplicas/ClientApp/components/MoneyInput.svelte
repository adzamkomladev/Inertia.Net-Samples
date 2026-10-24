<script lang="ts">
  import { cn } from '@/lib/utils'

  /** Dollar entry over a cents-long value: the API stays in cents, people type "6.50". */
  let { cents = $bindable(0), id, min = 0, max, invalid = false, class: className }: {
    cents?: number
    id: string
    min?: number
    max?: number
    invalid?: boolean
    class?: string
  } = $props()

  // While typing, show exactly what was typed; otherwise show the bound value, so outside changes (presets) appear.
  let draft = $state<string | null>(null)
  const update = (value: string) => {
    draft = value
    const parsed = Number.parseFloat(value)
    cents = Number.isFinite(parsed) ? Math.round(parsed * 100) : 0
  }
</script>

<div class={cn('ring-input bg-card focus-within:ring-ring flex h-11 items-center rounded-lg ring-1 ring-inset transition-shadow focus-within:ring-2 md:h-10', invalid && 'ring-destructive ring-2', className)}>
  <span class="text-muted-foreground pl-3 text-sm" aria-hidden="true">$</span>
  <input
    {id}
    type="number"
    inputmode="decimal"
    step="0.01"
    min={min / 100}
    max={max === undefined ? undefined : max / 100}
    value={draft ?? (cents / 100).toFixed(2)}
    oninput={(event) => update(event.currentTarget.value)}
    onblur={() => (draft = null)}
    aria-invalid={invalid || undefined}
    class="h-full w-full min-w-0 bg-transparent px-2 text-base tabular-nums outline-none md:text-sm"
  />
</div>
