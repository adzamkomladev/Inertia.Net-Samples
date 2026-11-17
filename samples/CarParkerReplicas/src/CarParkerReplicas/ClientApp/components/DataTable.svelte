<script lang="ts" module>
  import type { Snippet } from 'svelte'

  /** A column: header text and a cell renderer returning a row snippet or plain text. */
  export type Column = {
    key: string
    header: string
    cell?: (row: any) => string | number
    /** Rich cell: a row snippet (typed as a prop so svelte2tsx keeps it a Snippet). */
    snippet?: Snippet<[any]>
    align?: 'left' | 'right'
    /** Hide this column in the mobile card view (default: shown). */
    desktopOnly?: boolean
    /** Emphasize as the mobile card title. */
    primary?: boolean
    /** Compact phone rows: show this column on the right edge instead of in the meta line. */
    trailing?: boolean
  }
</script>

<script lang="ts">
  import { Link } from '@inertiajs/svelte'
  import ChevronRight from '@lucide/svelte/icons/chevron-right'
  import { cn } from '@/lib/utils'

  /**
   * Responsive data table: real `<table>` on desktop, stacked cards (or compact one-line rows) on phones.
   * Cell renderers return snippets (rich cells) or plain text; rows are keyed by `rowKey`. Detail routes
   * are Inertia links (client-side visits, hover prefetch); on phones the whole row is the tap target
   * through a stretched link, so each row keeps exactly one focusable control.
   */
  let {
    columns,
    rows,
    rowKey,
    rowHref,
    compact = false,
    emptyTitle = 'Nothing here yet',
    emptyDescription,
    class: className,
  }: {
    columns: Column[]
    rows: any[]
    rowKey: (row: any) => string
    rowHref?: (row: any) => string
    /** Phones: one dense line per row (title, meta line, trailing cells) instead of label/value cards. */
    compact?: boolean
    emptyTitle?: string
    emptyDescription?: string
    class?: string
  } = $props()

  const mobileColumns = $derived(columns.filter(c => !c.desktopOnly))
  const titleColumn = $derived(mobileColumns.find(c => c.primary) ?? mobileColumns[0]!)
  const metaColumns = $derived(mobileColumns.filter(c => c !== titleColumn && !c.trailing))
  const trailingColumns = $derived(mobileColumns.filter(c => c !== titleColumn && c.trailing))
  /** Interactive cell content stays clickable above a row's stretched link. The stretched link itself stays unpositioned so
   *  its ::after fills the whole row (the nearest positioned ancestor), not just the anchor. */
  const liftInteractive = '[&_a:not(.stretch-link)]:relative [&_a:not(.stretch-link)]:z-[1] [&_button]:relative [&_button]:z-[1]'
  const stretched = "stretch-link after:absolute after:inset-0 after:content-['']"
</script>

{#snippet cellContent(column: Column, row: any)}
  {#if column.snippet}
    {@render column.snippet(row)}
  {:else if column.cell}
    {column.cell(row)}
  {/if}
{/snippet}

{#if !rows.length}
  <div class="text-muted-foreground px-4 py-12 text-center text-sm">
    <p class="font-medium">{emptyTitle}</p>
    {#if emptyDescription}<p class="mt-1 text-sm">{emptyDescription}</p>{/if}
  </div>
{:else}
  <!-- Desktop table -->
  <div class={cn('hidden overflow-x-auto md:block', className)}>
    <table class="w-full border-collapse text-sm">
      <thead>
        <tr class="bg-muted/60 border-b">
          {#each columns as column (column.key)}
            <th scope="col" class={cn('text-muted-foreground px-4 py-3 text-xs font-semibold tracking-wide uppercase whitespace-nowrap', column.align === 'right' ? 'text-right' : 'text-left')}>
              {column.header}
            </th>
          {/each}
          {#if rowHref}<th scope="col" class="text-muted-foreground px-4 py-3 text-right text-xs font-semibold tracking-wide uppercase">Details</th>{/if}
        </tr>
      </thead>
      <tbody>
        {#each rows as row (rowKey(row))}
          <tr class="hover:bg-accent/40 border-b transition-colors last:border-b-0">
            {#each columns as column (column.key)}
              <td class={cn('px-4 py-3.5 align-middle', column.align === 'right' && 'text-right')}>
                {@render cellContent(column, row)}
              </td>
            {/each}
            {#if rowHref}
              <td class="px-3 py-2 text-right">
                <Link href={rowHref(row)} prefetch="hover" cacheFor="30s" class="group text-muted-foreground hover:border-primary/30 hover:bg-accent hover:text-foreground focus-visible:ring-ring inline-flex min-h-9 items-center gap-1 rounded-full border px-3 text-xs font-medium transition focus-visible:ring-2 focus-visible:outline-none">
                  Details <ChevronRight class="size-3.5 transition-transform group-hover:translate-x-0.5" aria-hidden="true" />
                </Link>
              </td>
            {/if}
          </tr>
        {/each}
      </tbody>
    </table>
  </div>

  {#if compact}
    <!-- Compact phone rows: one tappable line each -->
    <ul class="divide-border flex min-w-0 flex-col divide-y md:hidden">
      {#each rows as row (rowKey(row))}
        <li class={cn('relative flex min-h-16 min-w-0 items-center gap-3 px-4 py-3 transition-colors', rowHref && 'hover:bg-accent/40 has-[a:focus-visible]:ring-ring has-[a:focus-visible]:ring-2 has-[a:focus-visible]:ring-inset', liftInteractive)}>
          <div class="min-w-0 flex-1">
            <p class="truncate text-sm font-semibold">
              {#if rowHref}
                <Link href={rowHref(row)} prefetch="hover" cacheFor="30s" class={cn('focus-visible:underline focus-visible:outline-none', stretched)}>{@render cellContent(titleColumn, row)}</Link>
              {:else}
                {@render cellContent(titleColumn, row)}
              {/if}
            </p>
            {#if metaColumns.length}
              <p class="text-muted-foreground mt-0.5 truncate text-xs tabular-nums">
                {#each metaColumns as column, i (column.key)}{#if i}<span aria-hidden="true"> · </span>{/if}{@render cellContent(column, row)}{/each}
              </p>
            {/if}
          </div>
          {#if trailingColumns.length}
            <div class="flex shrink-0 flex-col items-end gap-1 text-right text-sm">
              {#each trailingColumns as column (column.key)}<span>{@render cellContent(column, row)}</span>{/each}
            </div>
          {/if}
          {#if rowHref}<ChevronRight class="text-muted-foreground size-4 shrink-0" aria-hidden="true" />{/if}
        </li>
      {/each}
    </ul>
  {:else}
    <!-- Mobile cards -->
    <div class="grid min-w-0 grid-cols-1 gap-3 md:hidden">
      {#each rows as row (rowKey(row))}
        <div class={cn('bg-card shadow-card relative min-w-0 rounded-xl p-4 transition-colors', rowHref && 'hover:bg-accent/30 has-[a:focus-visible]:ring-ring has-[a:focus-visible]:ring-2', liftInteractive)}>
          {#each mobileColumns as column (column.key)}
            <div class={cn('flex items-baseline justify-between gap-3', !column.primary && 'mt-1.5')}>
              <span class="text-muted-foreground shrink-0 text-xs">{column.header}</span>
              <span class={cn('min-w-0 flex-1 [overflow-wrap:anywhere] text-right', column.primary ? 'text-sm font-medium' : 'text-sm')}>
                {@render cellContent(column, row)}
              </span>
            </div>
          {/each}
          {#if rowHref}
            <Link href={rowHref(row)} prefetch="hover" cacheFor="30s" class={cn('text-primary mt-3 inline-flex min-h-11 items-center gap-1 text-sm font-semibold focus-visible:outline-none', stretched)}>
              View details <ChevronRight class="size-4" aria-hidden="true" />
            </Link>
          {/if}
        </div>
      {/each}
    </div>
  {/if}
{/if}
