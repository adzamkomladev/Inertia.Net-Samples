<script lang="ts">
  import { AlertDialog } from 'bits-ui'
  import type { Snippet } from 'svelte'
  import { buttonVariants } from '@/components/ui/button/variants'
  import { cn } from '@/lib/utils'

  let {
    trigger,
    title,
    description,
    confirmLabel = 'Delete',
    onConfirm,
    triggerClass,
  }: {
    trigger: Snippet
    title: string
    description: string
    confirmLabel?: string
    onConfirm: () => void
    triggerClass?: string
  } = $props()
</script>

<AlertDialog.Root>
  <AlertDialog.Trigger>
    {#snippet child({ props })}
      <button {...props} type="button" class={triggerClass}>{@render trigger()}</button>
    {/snippet}
  </AlertDialog.Trigger>
  <AlertDialog.Portal>
    <AlertDialog.Overlay class="data-[state=open]:animate-in data-[state=closed]:animate-out data-[state=closed]:fade-out-0 data-[state=open]:fade-in-0 fixed inset-0 z-50 bg-black/80" />
    <AlertDialog.Content
      class="bg-background data-[state=open]:animate-in data-[state=closed]:animate-out data-[state=closed]:fade-out-0 data-[state=open]:fade-in-0 data-[state=closed]:zoom-out-95 data-[state=open]:zoom-in-95 fixed top-[50%] left-[50%] z-50 grid w-full max-w-[calc(100%-2rem)] translate-x-[-50%] translate-y-[-50%] gap-4 rounded-lg border p-6 shadow-lg duration-200 sm:max-w-lg"
    >
      <div class="flex flex-col gap-2 text-center sm:text-left">
        <AlertDialog.Title class="text-lg font-semibold">{title}</AlertDialog.Title>
        <AlertDialog.Description class="text-muted-foreground text-sm">{description}</AlertDialog.Description>
      </div>
      <div class="flex flex-col-reverse gap-2 sm:flex-row sm:justify-end">
        <AlertDialog.Cancel class={cn(buttonVariants({ variant: 'outline' }), 'mt-2 sm:mt-0')}>Cancel</AlertDialog.Cancel>
        <AlertDialog.Action onclick={onConfirm} class={cn(buttonVariants(), 'mt-2 sm:mt-0')}>{confirmLabel}</AlertDialog.Action>
      </div>
    </AlertDialog.Content>
  </AlertDialog.Portal>
</AlertDialog.Root>
