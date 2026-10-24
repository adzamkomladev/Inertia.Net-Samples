<script lang="ts">
  import type { Snippet } from 'svelte'
  import { cn } from '@/lib/utils'

  let { for: htmlFor, class: className, children, ...rest }: { for?: string; class?: string; children?: Snippet; [key: string]: any } = $props()
</script>

<label
  for={htmlFor}
  data-slot="label"
  class={cn('flex items-center gap-2 text-sm leading-none font-medium select-none group-data-[disabled=true]:pointer-events-none group-data-[disabled=true]:opacity-50 peer-disabled:cursor-not-allowed peer-disabled:opacity-50', className)}
  {...rest}
>{@render children?.()}</label>
