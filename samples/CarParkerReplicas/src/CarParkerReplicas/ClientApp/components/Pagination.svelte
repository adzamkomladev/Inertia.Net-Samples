<script lang="ts">
  import ChevronLeft from '@lucide/svelte/icons/chevron-left'
  import ChevronRight from '@lucide/svelte/icons/chevron-right'
  import { router } from '@inertiajs/svelte'

  let { href, page, hasMore, params = {}, pageParam = 'page' }: {
    href: string
    page: number
    hasMore: boolean
    params?: Record<string, string | number | boolean | undefined>
    pageParam?: string
  } = $props()

  const go = (next: number) => router.get(href, { ...params, [pageParam]: next }, { preserveScroll: true, preserveState: true })
</script>

<nav aria-label="Pagination" class="flex items-center justify-between gap-3 border-t px-4 py-3">
  <button type="button" class="inline-flex min-h-10 items-center gap-2 rounded-full border px-4 text-sm font-medium transition hover:bg-accent disabled:pointer-events-none disabled:opacity-40" disabled={page <= 1} onclick={() => go(page - 1)}><ChevronLeft class="size-4" />Previous</button>
  <span class="text-sm text-muted-foreground">Page <span class="font-medium tabular-nums text-foreground">{page}</span></span>
  <button type="button" class="inline-flex min-h-10 items-center gap-2 rounded-full border px-4 text-sm font-medium transition hover:bg-accent disabled:pointer-events-none disabled:opacity-40" disabled={!hasMore} onclick={() => go(page + 1)}>Next<ChevronRight class="size-4" /></button>
</nav>
