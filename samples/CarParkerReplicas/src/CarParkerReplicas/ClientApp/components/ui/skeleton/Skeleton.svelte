<script lang="ts">
  import { cn } from '@/lib/utils'

  let { class: className, ...rest }: { class?: string; [key: string]: any } = $props()
</script>

<div data-slot="skeleton" class={cn('animate-pulse rounded-lg bg-foreground/8', className)} {...rest}></div>
