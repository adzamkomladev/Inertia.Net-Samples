<script lang="ts">
  import { router } from '@inertiajs/svelte'
  import { Search, X } from '@lucide/svelte'
  import Input from '@/components/ui/input/Input.svelte'
  import { debounce, cn } from '@/lib/utils'

  let { value = $bindable(''), class: className }: { value?: string; class?: string } = $props()

  const run = (search: string) =>
    router.get('/posts', search ? { q: search } : {}, { preserveState: true, replace: true, preserveScroll: true })
  const debounced = debounce(run, 300)

  function clear() {
    value = ''
    run('')
  }
</script>

<div class={cn('relative w-full', className)} role="search">
  <Search class="text-muted-foreground pointer-events-none absolute top-1/2 left-3 size-4 -translate-y-1/2" />
  <Input
    bind:value
    type="search"
    placeholder="Search"
    aria-label="Search posts"
    autocomplete="off"
    class="bg-muted focus-visible:bg-background h-9 rounded-lg border-transparent pr-9 pl-9 shadow-none [&::-webkit-search-cancel-button]:hidden"
    oninput={() => debounced(value.trim())}
    onkeydown={(event: KeyboardEvent) => { if (event.key === 'Escape') clear() }}
  />
  {#if value}
    <button
      type="button" aria-label="Clear search"
      class="text-muted-foreground hover:text-foreground focus-visible:ring-ring absolute top-1/2 right-2 -translate-y-1/2 rounded p-1 outline-none focus-visible:ring-2"
      onclick={clear}
    >
      <X class="size-4" />
    </button>
  {/if}
</div>
