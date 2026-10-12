<script lang="ts">
  import { cn } from '@/lib/utils'

  let { class: className, ...rest }: { class?: string; [key: string]: any } = $props()
</script>

<div data-slot="skeleton" class={cn('animate-pulse rounded-md bg-primary/10', className)} {...rest}></div>
