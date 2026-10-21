<script lang="ts">
  import type { Component, Snippet } from 'svelte'
  import { cn } from '@/lib/utils'

  /** Quiet empty state: icon tile, title, body, optional action — used across every console. */
  let {
    icon,
    title,
    description,
    class: className,
    children,
  }: {
    icon: Component<Record<string, any>>
    title?: string
    description?: string
    class?: string
    children?: Snippet
  } = $props()

  // svelte-ignore state_referenced_locally
  const Icon = icon
</script>

<div class={cn('animate-rise flex flex-col items-center gap-4 px-6 py-16 text-center', className)}>
  <span class="bg-card text-muted-foreground shadow-card flex size-14 items-center justify-center rounded-2xl border">
    <Icon class="size-6" strokeWidth={1.5} aria-hidden="true" />
  </span>
  <div class="max-w-sm space-y-1.5">
    {#if title}<p class="font-display text-base font-semibold">{title}</p>{/if}
    {#if description}<p class="text-muted-foreground text-sm leading-6">{description}</p>{/if}
  </div>
  {@render children?.()}
</div>
