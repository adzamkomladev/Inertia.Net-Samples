<script lang="ts" module>
  import type { Component } from 'svelte'

  export type DropdownEntry =
    | { type: 'separator' }
    | { type: 'label'; label: string; sublabel?: string }
    | { type: 'item'; label: string; icon?: Component<any>; href?: string; onSelect?: () => void; disabled?: boolean }
</script>

<script lang="ts">
  import { DropdownMenu } from 'bits-ui'
  import { Link } from '@inertiajs/svelte'
  import type { Snippet } from 'svelte'
  import { cn } from '@/lib/utils'

  let {
    entries,
    trigger,
    align = 'start',
    side = 'bottom',
    triggerClass,
    triggerLabel,
    contentClass,
  }: {
    entries: DropdownEntry[]
    trigger: Snippet
    align?: 'start' | 'center' | 'end'
    side?: 'top' | 'right' | 'bottom' | 'left'
    triggerClass?: string
    triggerLabel?: string
    contentClass?: string
  } = $props()

  const itemClass =
    "data-[highlighted]:bg-accent data-[highlighted]:text-accent-foreground relative flex cursor-default items-center gap-2 rounded-sm px-2 py-1.5 text-sm outline-hidden select-none data-[disabled]:pointer-events-none data-[disabled]:opacity-50 [&_svg]:pointer-events-none [&_svg]:shrink-0 [&_svg:not([class*='size-'])]:size-4 [&_svg:not([class*='text-'])]:text-muted-foreground"
</script>

<DropdownMenu.Root>
  <DropdownMenu.Trigger>
    {#snippet child({ props })}
      <button {...props} type="button" aria-label={triggerLabel} class={cn('outline-none focus-visible:ring-2 focus-visible:ring-ring', triggerClass)}>{@render trigger()}</button>
    {/snippet}
  </DropdownMenu.Trigger>
  <DropdownMenu.Portal>
    <DropdownMenu.Content
      {align}
      {side}
      sideOffset={6}
      class={cn(
        'bg-popover text-popover-foreground data-[state=open]:animate-in data-[state=closed]:animate-out data-[state=closed]:fade-out-0 data-[state=open]:fade-in-0 data-[state=closed]:zoom-out-95 data-[state=open]:zoom-in-95 data-[side=bottom]:slide-in-from-top-2 data-[side=left]:slide-in-from-right-2 data-[side=right]:slide-in-from-left-2 data-[side=top]:slide-in-from-bottom-2 z-50 max-h-[min(24rem,var(--bits-dropdown-menu-content-available-height))] min-w-[8rem] origin-(--bits-dropdown-menu-content-transform-origin) overflow-x-hidden overflow-y-auto rounded-md border p-1 shadow-md',
        contentClass,
      )}
    >
      {#each entries as entry, index (index)}
        {#if entry.type === 'separator'}
          <DropdownMenu.Separator class="bg-border -mx-1 my-1 h-px" />
        {:else if entry.type === 'label'}
          <DropdownMenu.GroupHeading class="px-2 py-1.5">
            <div class="truncate text-sm font-medium">{entry.label}</div>
            {#if entry.sublabel}<div class="text-muted-foreground truncate text-xs">{entry.sublabel}</div>{/if}
          </DropdownMenu.GroupHeading>
        {:else if entry.href}
          <DropdownMenu.Item disabled={entry.disabled}>
            {#snippet child({ props })}
              <Link href={entry.href} {...props} class={cn('', itemClass)}>
                {#if entry.icon}<entry.icon class="size-4" />{/if}{entry.label}
              </Link>
            {/snippet}
          </DropdownMenu.Item>
        {:else}
          <DropdownMenu.Item disabled={entry.disabled} onSelect={() => entry.onSelect?.()} class={itemClass}>
            {#if entry.icon}<entry.icon class="size-4" />{/if}{entry.label}
          </DropdownMenu.Item>
        {/if}
      {/each}
    </DropdownMenu.Content>
  </DropdownMenu.Portal>
</DropdownMenu.Root>
