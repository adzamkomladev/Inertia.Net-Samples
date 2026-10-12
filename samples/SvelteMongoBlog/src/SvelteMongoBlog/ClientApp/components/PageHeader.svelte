<script lang="ts">
  import type { Snippet } from 'svelte'
  import { cn } from '@/lib/utils'

  let { title, description, actions, class: className }: { title?: string | Snippet; description?: string | Snippet; actions?: Snippet; class?: string } = $props()
</script>

<!-- Title block shared by every app page: title, optional description and right-aligned actions. -->
<header class={cn('flex flex-wrap items-end justify-between gap-x-4 gap-y-3', className)}>
  <div class="min-w-0 space-y-1">
    <h1 class="text-2xl font-semibold tracking-tight text-balance">
      {#if typeof title === 'function'}{@render title()}{:else}{title}{/if}
    </h1>
    {#if description !== undefined || typeof description === 'function'}
      <p class="text-muted-foreground text-[15px]">
        {#if typeof description === 'function'}{@render description()}{:else}{description}{/if}
      </p>
    {/if}
  </div>
  {#if actions}<div class="flex shrink-0 items-center gap-2">{@render actions()}</div>{/if}
</header>
