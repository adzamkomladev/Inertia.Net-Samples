<script lang="ts">
  import Search from '@lucide/svelte/icons/search'
  import X from '@lucide/svelte/icons/x'
  let { value = $bindable(''), label = 'Search', placeholder = 'Search…' }: { value?: string; label?: string; placeholder?: string } = $props()
</script>
<div class="relative min-w-0 flex-1">
  <Search class="text-muted-foreground pointer-events-none absolute left-3.5 top-1/2 size-4 -translate-y-1/2" aria-hidden="true" />
  <input type="search" aria-label={label} {placeholder} bind:value class="bg-card border-input placeholder:text-muted-foreground focus-visible:ring-ring h-11 w-full rounded-lg border pl-10 pr-12 outline-none focus-visible:ring-2 [&::-webkit-search-cancel-button]:appearance-none" />
  {#if value}<button type="button" aria-label="Clear search" onclick={() => value = ''} class="text-muted-foreground absolute right-0 top-0 grid size-11 place-items-center"><X class="size-4" /></button>{/if}
</div>
