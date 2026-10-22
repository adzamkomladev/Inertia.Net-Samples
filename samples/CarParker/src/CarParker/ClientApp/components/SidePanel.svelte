<script lang="ts">
  import { Dialog } from 'bits-ui'
  import X from '@lucide/svelte/icons/x'
  import type { Snippet } from 'svelte'
  let { open = $bindable(false), title, description, children }: { open?: boolean; title: string; description: string; children: Snippet } = $props()
</script>
<Dialog.Root bind:open>
  <Dialog.Portal>
    <Dialog.Overlay class="fixed inset-0 z-50 bg-black/45 backdrop-blur-sm" />
    <Dialog.Content class="bg-card fixed inset-y-0 right-0 z-50 flex w-full max-w-xl flex-col border-l shadow-2xl outline-none">
      <div class="flex items-start gap-4 border-b p-6">
        <div class="min-w-0 flex-1">
          <Dialog.Title class="text-2xl font-semibold">{title}</Dialog.Title>
          <Dialog.Description class="text-muted-foreground mt-2 text-sm leading-6">{description}</Dialog.Description>
        </div>
        <Dialog.Close aria-label="Close panel" class="hover:bg-muted grid size-11 shrink-0 place-items-center rounded-lg"><X class="size-5" /></Dialog.Close>
      </div>
      <div class="min-h-0 flex-1 overflow-y-auto overscroll-contain p-6">{@render children()}</div>
    </Dialog.Content>
  </Dialog.Portal>
</Dialog.Root>
