<script lang="ts">
  import { Avatar } from 'bits-ui'
  import { cn } from '@/lib/utils'

  let {
    src,
    alt = '',
    fallback,
    class: className,
    fallbackClass,
    ...rest
  }: { src?: string | null; alt?: string; fallback?: string; class?: string; fallbackClass?: string; [key: string]: any } = $props()
</script>

<Avatar.Root class={cn('relative flex size-8 shrink-0 overflow-hidden rounded-full', className)} {...rest}>
  {#if src}
    <Avatar.Image {src} {alt} class="aspect-square size-full" />
  {/if}
  <Avatar.Fallback class={cn('bg-muted flex size-full items-center justify-center rounded-full', fallbackClass)}>{fallback}</Avatar.Fallback>
</Avatar.Root>
