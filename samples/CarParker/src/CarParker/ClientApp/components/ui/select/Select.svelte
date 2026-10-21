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
</script>

<Select.Root type="single" bind:value {disabled} {onValueChange}>
  <Select.Trigger
    {id}
    data-slot="select-trigger"
    aria-label={ariaLabel}
    class={cn(
      "border-input data-[placeholder]:text-muted-foreground [&_svg:not([class*='text-'])]:text-muted-foreground focus-visible:border-ring focus-visible:ring-ring/50 aria-invalid:ring-destructive/20 dark:aria-invalid:ring-destructive/40 aria-invalid:border-destructive dark:bg-input/30 dark:hover:bg-input/50 flex h-9 w-fit items-center justify-between gap-2 rounded-md border bg-transparent px-3 py-2 text-sm whitespace-nowrap shadow-xs transition-[color,box-shadow] outline-none focus-visible:ring-3 disabled:cursor-not-allowed disabled:opacity-50 [&_svg]:pointer-events-none [&_svg]:shrink-0 [&_svg:not([class*='size-'])]:size-4",
      className,
    )}
  >
    <Select.Value {placeholder} />
    <ChevronDown class="size-4 opacity-50" />
  </Select.Trigger>
  <Select.Portal>
    <Select.Content
      sideOffset={4}
      class="bg-popover text-popover-foreground data-[state=open]:animate-in data-[state=closed]:animate-out data-[state=closed]:fade-out-0 data-[state=open]:fade-in-0 data-[state=closed]:zoom-out-95 data-[state=open]:zoom-in-95 z-50 max-h-[min(24rem,var(--bits-select-content-available-height))] min-w-[8rem] origin-(--bits-select-content-transform-origin) overflow-x-hidden overflow-y-auto rounded-md border p-1 shadow-md"
    >
      <Select.ScrollUpButton class="flex w-full items-center justify-center py-1">
        <ChevronDown class="size-4 rotate-180" />
      </Select.ScrollUpButton>
      <Select.Viewport class="p-1">
        {#each items as item (item.value)}
          <Select.Item
            value={item.value}
            label={item.label}
            class={cn(
              "group/item data-[highlighted]:bg-accent data-[highlighted]:text-accent-foreground [&_svg:not([class*='text-'])]:text-muted-foreground relative flex w-full cursor-default items-center gap-2 rounded-sm py-1.5 pr-8 pl-2 text-sm outline-hidden select-none data-[disabled]:pointer-events-none data-[disabled]:opacity-50 [&_svg]:pointer-events-none [&_svg]:shrink-0 [&_svg:not([class*='size-'])]:size-4",
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
