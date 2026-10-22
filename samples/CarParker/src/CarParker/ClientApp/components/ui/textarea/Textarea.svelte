<script lang="ts">
  import { cn } from '@/lib/utils'

  let {
    value = $bindable<string | number | null | undefined>(),
    class: className,
    autosize = false,
    maxHeight = 240,
    ...rest
  }: { value?: string | number | null; class?: string; autosize?: boolean; maxHeight?: number; [key: string]: any } = $props()

  let el: HTMLTextAreaElement | undefined = $state()
  $effect(() => {
    value
    if (!autosize || !el) return
    el.style.height = 'auto'
    el.style.height = `${Math.min(el.scrollHeight, maxHeight)}px`
  })
</script>

<textarea
  bind:this={el}
  bind:value
  data-slot="textarea"
  class={cn(
    'bg-card ring-input placeholder:text-muted-foreground focus-visible:ring-ring aria-invalid:ring-destructive flex field-sizing-content min-h-16 w-full rounded-lg px-3 py-2.5 text-base ring-1 ring-inset transition-shadow outline-none focus-visible:ring-2 disabled:cursor-not-allowed disabled:opacity-50 md:text-sm',
    className,
  )}
  {...rest}
></textarea>

