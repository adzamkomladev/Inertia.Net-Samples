<script lang="ts">
  import { Select } from 'bits-ui'
  import { Check, ChevronDown } from '@lucide/svelte'
  import { cn } from '@/lib/utils'

  let {
    value = $bindable(),
    items,
    id,
    placeholder,
    ariaLabel,
    class: className,
    disabled,
    onValueChange,
  }: {
    value?: string
    items: { value: string; label: string }[]
    id?: string
    placeholder?: string
    ariaLabel?: string
    class?: string
    disabled?: boolean
    onValueChange?: (value: string) => void
  } = $props()

  // bits-ui treats '' as "nothing selected" and shows no label, so an empty option value ("Any", "All") rides as a sentinel.
  const NONE = '__none__'
  const inner = $derived(value === '' || value == null ? (items.some(i => i.value === '') ? NONE : undefined) : value)
  const innerItems = $derived(items.map(i => (i.value === '' ? { ...i, value: NONE } : i)))
  const change = (next: string) => {
    value = next === NONE ? '' : next
    onValueChange?.(value)
  }
</script>

<Select.Root type="single" value={inner} onValueChange={change} {disabled} items={innerItems}>
  <Select.Trigger
    {id}
    data-slot="select-trigger"
    aria-label={ariaLabel}
    class={cn(
      "bg-card ring-input data-[placeholder]:text-muted-foreground [&_svg:not([class*='text-'])]:text-muted-foreground focus-visible:ring-ring aria-invalid:ring-destructive flex h-11 w-fit items-center justify-between gap-2 rounded-lg px-3 py-2 text-base ring-1 ring-inset whitespace-nowrap transition-shadow outline-none focus-visible:ring-2 md:h-10 md:text-sm disabled:cursor-not-allowed disabled:opacity-50 [&_svg]:pointer-events-none [&_svg]:shrink-0 [&_svg:not([class*='size-'])]:size-4",
      className,
    )}
  >
    <Select.Value {placeholder} />
    <ChevronDown class="size-4 opacity-50" />
  </Select.Trigger>
  <Select.Portal>
    <Select.Content
      sideOffset={4}
      class="bg-popover text-popover-foreground data-[state=open]:animate-in data-[state=closed]:animate-out data-[state=closed]:fade-out-0 data-[state=open]:fade-in-0 data-[state=closed]:zoom-out-95 data-[state=open]:zoom-in-95 z-50 max-h-[min(24rem,var(--bits-select-content-available-height))] min-w-[8rem] origin-(--bits-select-content-transform-origin) overflow-x-hidden overflow-y-auto rounded-lg p-1 shadow-pop ring-1 ring-foreground/10"
    >
      <Select.ScrollUpButton class="flex w-full items-center justify-center py-1">
        <ChevronDown class="size-4 rotate-180" />
      </Select.ScrollUpButton>
      <Select.Viewport class="p-1">
        {#each innerItems as item (item.value)}
          <Select.Item
            value={item.value}
            label={item.label}
            class={cn(
              "group/item data-[highlighted]:bg-accent data-[highlighted]:text-accent-foreground [&_svg:not([class*='text-'])]:text-muted-foreground relative flex w-full cursor-default items-center gap-2 rounded-md py-2.5 pr-8 pl-2.5 text-sm md:py-2 outline-hidden select-none data-[disabled]:pointer-events-none data-[disabled]:opacity-50 [&_svg]:pointer-events-none [&_svg]:shrink-0 [&_svg:not([class*='size-'])]:size-4",
            )}
          >
            <span class="absolute right-2 flex size-3.5 items-center justify-center">
              <Check class="size-4 opacity-0 group-data-[selected]/item:opacity-100" />
            </span>
            {item.label}
          </Select.Item>
        {/each}
      </Select.Viewport>
      <Select.ScrollDownButton class="flex w-full items-center justify-center py-1">
        <ChevronDown class="size-4" />
      </Select.ScrollDownButton>
    </Select.Content>
  </Select.Portal>
</Select.Root>
