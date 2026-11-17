<script lang="ts">
  import { Dialog } from 'bits-ui'
  import X from '@lucide/svelte/icons/x'
  import type { Snippet } from 'svelte'
  import { cn } from '@/lib/utils'

  /** Right-hand sheet for create/edit flows and mobile navigation; `footer` pins actions to the bottom. */
  let {
    open = $bindable(false),
    side = 'right',
    title,
    description,
    children,
    footer,
  }: { open?: boolean; side?: 'left' | 'right'; title: string; description: string; children: Snippet; footer?: Snippet } = $props()
</script>
<Dialog.Root bind:open>
  <Dialog.Portal>
    <Dialog.Overlay class="data-[state=open]:animate-in data-[state=closed]:animate-out data-[state=closed]:fade-out-0 data-[state=open]:fade-in-0 fixed inset-0 z-50 bg-black/45 backdrop-blur-sm duration-200" />
    <Dialog.Content class={cn('bg-card data-[state=open]:animate-in data-[state=closed]:animate-out fixed inset-y-0 z-50 flex w-full max-w-full flex-col shadow-2xl outline-none duration-300 ease-(--ease-glide)', side === 'left' ? 'data-[state=closed]:slide-out-to-left data-[state=open]:slide-in-from-left left-0 w-[85vw] border-r sm:max-w-sm sm:rounded-r-2xl' : 'data-[state=closed]:slide-out-to-right data-[state=open]:slide-in-from-right right-0 border-l sm:max-w-xl sm:rounded-l-2xl')}>
      <div class="flex items-start gap-4 border-b p-4 pt-[max(1rem,env(safe-area-inset-top))] sm:p-6">
        <div class="min-w-0 flex-1">
          <Dialog.Title class="font-display text-2xl font-semibold tracking-tight">{title}</Dialog.Title>
          <Dialog.Description class="text-muted-foreground mt-1.5 text-sm leading-6">{description}</Dialog.Description>
        </div>
        <Dialog.Close aria-label="Close panel" class="hover:bg-muted grid size-11 shrink-0 place-items-center rounded-lg transition-[background-color,transform] hover:rotate-90 active:scale-95"><X class="size-5" /></Dialog.Close>
      </div>
      <div class="min-h-0 flex-1 overflow-y-auto overscroll-contain p-4 sm:p-6">{@render children()}</div>
      {#if footer}<div class="bg-card/95 flex items-center justify-end gap-2 border-t px-4 py-4 pb-[max(1rem,env(safe-area-inset-bottom))] backdrop-blur sm:px-6">{@render footer()}</div>{/if}
    </Dialog.Content>
  </Dialog.Portal>
</Dialog.Root>
