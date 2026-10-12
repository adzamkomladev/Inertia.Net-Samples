<script lang="ts">
  import type { Component, Snippet } from 'svelte'
  import { cn } from '@/lib/utils'

  let { icon, title, description, class: className, children }: { icon: Component<Record<string, any>>; title?: string; description?: string; class?: string; children?: Snippet } = $props()

  // svelte-ignore state_referenced_locally
  const Icon = icon
</script>

<!-- Quiet empty state used by feeds, people, search results and comments. -->
<div class={cn('animate-rise flex flex-col items-center gap-4 px-6 py-20 text-center', className)}>
  <span class="bg-card text-muted-foreground flex size-12 items-center justify-center rounded-2xl border shadow-xs">
    <Icon class="size-5" strokeWidth={1.75} aria-hidden="true" />
  </span>
  <div class="max-w-sm space-y-1.5">
    {#if title}<p class="font-medium">{title}</p>{/if}
    {#if description}<p class="text-muted-foreground text-sm leading-6">{description}</p>{/if}
  </div>
  {@render children?.()}
</div>
