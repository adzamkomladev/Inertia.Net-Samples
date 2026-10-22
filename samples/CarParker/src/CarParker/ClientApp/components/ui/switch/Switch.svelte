<script lang="ts">
  import { Switch } from 'bits-ui'
  import { cn } from '@/lib/utils'

  let { checked = $bindable(false), class: className, ...rest }: { checked?: boolean; class?: string; [key: string]: any } = $props()
</script>

<Switch.Root
  bind:checked
  data-slot="switch"
  class={cn(
    'peer data-[state=checked]:bg-primary data-[state=unchecked]:bg-input focus-visible:ring-ring inline-flex h-6 w-11 shrink-0 items-center rounded-full p-0.5 transition-colors outline-none focus-visible:ring-2 focus-visible:ring-offset-2 focus-visible:ring-offset-background disabled:cursor-not-allowed disabled:opacity-50',
    className,
  )}
  {...rest}
>
  <Switch.Thumb class="bg-white shadow-xs pointer-events-none block size-5 rounded-full ring-0 transition-transform data-[state=checked]:translate-x-5 data-[state=unchecked]:translate-x-0" />
</Switch.Root>
