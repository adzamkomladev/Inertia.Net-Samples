<script lang="ts" module>
  import type { Snippet } from 'svelte'

  /** A column: header text and a cell renderer returning a snippet or plain text. */
  export type Column<T> = {
    key: string
    header: string
    cell: (row: T) => Snippet | string | number
    align?: 'left' | 'right'
    /** Hide this column in the mobile card view (default: shown). */
    desktopOnly?: boolean
    /** Emphasize as the mobile card title. */
    primary?: boolean
  }
</script>

<script lang="ts" generics="T">
  import { cn } from '@/lib/utils'

  /**
   * Responsive data table: real `<table>` on desktop, stacked cards on mobile.
   * Cell renderers return snippets (rich cells) or plain text; rows are keyed by
   * `rowKey` and become links when `rowHref` is set.
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
    columns: Column<T>[]
    rows: T[]
    rowKey: (row: T) => string
    rowHref?: (row: T) => string
    emptyTitle?: string
    emptyDescription?: string
    class?: string
  } = $props()

  const mobileColumns = $derived(columns.filter(c => !c.desktopOnly))
</script>

{#snippet cellContent(column: Column<T>, row: T)}
  {@const result = column.cell(row)}
  {#if typeof result === 'object' && result !== null && !(result instanceof Date) && 'render' in result}
    {@render (result as Snippet)}
  {:else}
    {result}
  {/if}
{/snippet}

{#if !rows.length}
  <div class="text-muted-foreground px-4 py-12 text-center text-sm">
    <p class="font-medium">{emptyTitle}</p>
    {#if emptyDescription}<p class="mt-1 text-[13px]">{emptyDescription}</p>{/if}
  </div>
{:else}
  <!-- Desktop table -->
  <div class={cn('hidden overflow-x-auto md:block', className)}>
    <table class="w-full border-collapse text-sm">
      <thead>
        <tr class="border-b">
          {#each columns as column (column.key)}
            <th scope="col" class={cn('text-muted-foreground px-4 py-2.5 text-xs font-medium whitespace-nowrap', column.align === 'right' ? 'text-right' : 'text-left')}>
              {column.header}
            </th>
          {/each}
        </tr>
      </thead>
      <tbody>
        {#each rows as row (rowKey(row))}
          <tr class="hover:bg-accent/50 border-b transition-colors last:border-b-0">
            {#each columns as column, i (column.key)}
              <td class={cn('px-4 py-3 align-middle', column.align === 'right' && 'text-right')}>
                {#if rowHref && i === 0}
                  <a href={rowHref(row)} class="hover:underline underline-offset-4">{@render cellContent(column, row)}</a>
                {:else}
                  {@render cellContent(column, row)}
                {/if}
              </td>
            {/each}
          </tr>
        {/each}
      </tbody>
    </table>
  </div>

  <!-- Mobile cards -->
  <div class="grid gap-3 md:hidden">
    {#each rows as row (rowKey(row))}
      <div class="bg-card relative rounded-2xl border p-4">
        {#each mobileColumns as column (column.key)}
          <div class={cn('flex items-baseline justify-between gap-3', !column.primary && 'mt-1.5')}>
            <span class="text-muted-foreground shrink-0 text-xs">{column.header}</span>
            <span class={cn('min-w-0 text-right', column.primary ? 'text-sm font-medium' : 'text-[13px]')}>
              {@render cellContent(column, row)}
            </span>
          </div>
        {/each}
        {#if rowHref}
          <a href={rowHref(row)} class="absolute inset-0 rounded-2xl" aria-label="Open"></a>
        {/if}
      </div>
    {/each}
  </div>
{/if}
