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
    'border-input placeholder:text-muted-foreground focus-visible:border-ring focus-visible:ring-ring/50 aria-invalid:ring-destructive/20 dark:aria-invalid:ring-destructive/40 aria-invalid:border-destructive dark:bg-input/30 flex field-sizing-content min-h-16 w-full rounded-md border bg-transparent px-3 py-2 text-base shadow-xs transition-[color,box-shadow] outline-none focus-visible:ring-3 disabled:cursor-not-allowed disabled:opacity-50 md:text-sm',
    className,
  )}
  {...rest}
></textarea>

