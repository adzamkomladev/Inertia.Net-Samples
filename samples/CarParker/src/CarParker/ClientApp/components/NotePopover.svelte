<script lang="ts">
  import { Popover } from 'bits-ui'
  import type { Snippet } from 'svelte'
  import Loader2 from '@lucide/svelte/icons/loader-circle'
  import Button from '@/components/ui/button/Button.svelte'
  import Textarea from '@/components/ui/textarea/Textarea.svelte'
  import { buttonVariants, type ButtonVariant } from '@/components/ui/button/variants'

  /**
   * A button that opens a small note form next to it: resolve an incident, explain a decision.
   * Each instance keeps its own draft, so notes never leak between rows.
   */
  let {
    trigger,
    title,
    placeholder,
    confirmLabel,
    maxlength = 200,
    required = false,
    processing = false,
    variant = 'default',
    onsubmit,
  }: {
    trigger: Snippet
    title: string
    placeholder?: string
    confirmLabel: string
    maxlength?: number
    required?: boolean
    processing?: boolean
    variant?: ButtonVariant
    onsubmit: (note: string, close: () => void) => void
  } = $props()

  let open = $state(false)
  let note = $state('')
  const id = $props.id()
</script>

<Popover.Root bind:open>
  <Popover.Trigger class={buttonVariants({ variant, size: 'sm' })}>{@render trigger()}</Popover.Trigger>
  <Popover.Portal>
    <Popover.Content
      align="end"
      sideOffset={8}
      class="bg-popover text-popover-foreground data-[state=open]:animate-in data-[state=closed]:animate-out data-[state=closed]:fade-out-0 data-[state=open]:fade-in-0 data-[state=closed]:zoom-out-95 data-[state=open]:zoom-in-95 data-[side=bottom]:slide-in-from-top-2 data-[side=top]:slide-in-from-bottom-2 z-50 w-80 max-w-[calc(100vw-2rem)] rounded-xl border p-4 shadow-pop outline-none"
    >
      <form class="grid gap-3" onsubmit={(event) => { event.preventDefault(); onsubmit(note.trim(), () => { open = false; note = '' }) }}>
        <label for={id} class="text-sm font-semibold">{title}</label>
        <Textarea {id} bind:value={note} {placeholder} {maxlength} rows={3} autosize {required} />
        <div class="flex items-center justify-between gap-2">
          <span class="text-muted-foreground text-xs tabular-nums">{note.length}/{maxlength}</span>
          <div class="flex gap-2">
            <Popover.Close class={buttonVariants({ variant: 'ghost', size: 'sm' })}>Cancel</Popover.Close>
            <Button type="submit" size="sm" disabled={processing || (required && !note.trim())}>
              {#if processing}<Loader2 class="animate-spin" aria-hidden="true" />{/if}{confirmLabel}
            </Button>
          </div>
        </div>
      </form>
    </Popover.Content>
  </Popover.Portal>
</Popover.Root>
