<script lang="ts">
  import { cn } from '@/lib/utils'

  let { value = $bindable<string | number | null | undefined>(), class: className, ...rest }: { value?: string | number | null; class?: string; [key: string]: any } = $props()
</script>

<input
  bind:value
  data-slot="input"
  class={cn(
    'file:text-foreground placeholder:text-muted-foreground selection:bg-primary selection:text-primary-foreground bg-card ring-input h-11 w-full min-w-0 rounded-lg px-3 py-1 text-base ring-1 ring-inset md:h-10 transition-shadow outline-none file:inline-flex file:h-7 file:border-0 file:bg-transparent file:text-sm file:font-medium disabled:pointer-events-none disabled:cursor-not-allowed disabled:opacity-50 md:text-sm',
    'focus-visible:ring-ring focus-visible:ring-2',
    'aria-invalid:ring-destructive aria-invalid:ring-2',
    className,
  )}
  {...rest}
/>
