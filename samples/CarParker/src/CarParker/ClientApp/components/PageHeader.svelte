<script lang="ts">
  import type { Snippet } from 'svelte'
  import { cn } from '@/lib/utils'

  /** Title block shared by every page: title, optional description, right-aligned actions. */
  let {
    title,
    description,
    actions,
    class: className,
  }: {
    title?: string | Snippet
    description?: string | Snippet
    actions?: Snippet
    class?: string
  } = $props()
</script>

<header class={cn('flex flex-wrap items-end justify-between gap-x-4 gap-y-3', className)}>
  <div class="min-w-0 space-y-1">
    <h1 class="font-display text-3xl font-bold tracking-tight text-balance">
      {#if typeof title === 'function'}{@render title()}{:else}{title}{/if}
    </h1>
    {#if description !== undefined || typeof description === 'function'}
      <p class="text-muted-foreground text-base">
        {#if typeof description === 'function'}{@render description()}{:else}{description}{/if}
      </p>
    {/if}
  </div>
  {#if actions}<div class="flex shrink-0 items-center gap-2">{@render actions()}</div>{/if}
</header>
