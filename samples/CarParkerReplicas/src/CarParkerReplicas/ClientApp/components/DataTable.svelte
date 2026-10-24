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
  }
</script>

<script lang="ts">
  import { cn } from '@/lib/utils'
  import ArrowUpRight from '@lucide/svelte/icons/arrow-up-right'

  /**
   * Responsive data table: real `<table>` on desktop, stacked cards on mobile.
   * Cell renderers return snippets (rich cells) or plain text; rows are keyed by
   * `rowKey`; detail routes have a quiet button instead of an underlined cell link.
   */
  let {
    columns,
    rows,
    rowKey,
    rowHref,
    emptyTitle = 'Nothing here yet',
    emptyDescription,
    class: className,
  }: {
    columns: Column[]
    rows: any[]
    rowKey: (row: any) => string
    rowHref?: (row: any) => string
    emptyTitle?: string
    emptyDescription?: string
    class?: string
  } = $props()

  const mobileColumns = $derived(columns.filter(c => !c.desktopOnly))
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
            {#if rowHref}<td class="px-3 py-2 text-right"><a href={rowHref(row)} class="group inline-flex min-h-9 items-center gap-1 rounded-full border px-3 text-xs font-medium text-muted-foreground transition hover:border-primary/30 hover:bg-accent hover:text-foreground focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring" aria-label="View details">Details <ArrowUpRight class="size-3.5 transition-transform group-hover:translate-x-0.5 group-hover:-translate-y-0.5" /></a></td>{/if}
          </tr>
        {/each}
      </tbody>
    </table>
  </div>

  <!-- Mobile cards -->
  <div class="grid min-w-0 grid-cols-1 gap-3 md:hidden">
    {#each rows as row (rowKey(row))}
      <div class="bg-card shadow-card min-w-0 rounded-xl p-4 transition-colors hover:border-primary/30 hover:bg-accent/30">
        {#each mobileColumns as column (column.key)}
          <div class={cn('flex items-baseline justify-between gap-3', !column.primary && 'mt-1.5')}>
            <span class="text-muted-foreground shrink-0 text-xs">{column.header}</span>
            <span class={cn('min-w-0 flex-1 [overflow-wrap:anywhere] text-right', column.primary ? 'text-sm font-medium' : 'text-sm')}>
              {@render cellContent(column, row)}
            </span>
          </div>
        {/each}
        {#if rowHref}<a href={rowHref(row)} class="mt-4 inline-flex min-h-9 items-center gap-1 rounded-full border px-3 text-xs font-medium text-muted-foreground transition hover:bg-accent hover:text-foreground" aria-label="View details">View details <ArrowUpRight class="size-3.5" /></a>{/if}
      </div>
    {/each}
  </div>
{/if}
