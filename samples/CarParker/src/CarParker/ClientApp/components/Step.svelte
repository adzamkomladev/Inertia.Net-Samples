<script lang="ts">
  import type { Snippet } from 'svelte'
  import { Check } from '@lucide/svelte'
  import { cn } from '@/lib/utils'

  /** One numbered section of a multi-part flow; the number turns into a check once complete. */
  let {
    number,
    title,
    summary,
    done = false,
    id,
    children,
  }: {
    number: number
    title: string
    /** What the user chose, shown beside the title once the step is done. */
    summary?: string
    done?: boolean
    id?: string
    children?: Snippet
  } = $props()
</script>

<section {id} class="bg-card shadow-card scroll-mt-24 rounded-xl p-4 sm:p-6" aria-labelledby={`${id ?? `step-${number}`}-title`}>
  <header class="flex items-center gap-3">
    <span
      class={cn(
        'font-display grid size-9 shrink-0 place-items-center rounded-full text-base font-bold transition-colors',
        done ? 'bg-spot-free text-spot-free-foreground' : 'bg-primary text-primary-foreground',
      )}
      aria-hidden="true"
    >
      {#if done}<Check class="size-5" strokeWidth={3} />{:else}{number}{/if}
    </span>
    <h2 id={`${id ?? `step-${number}`}-title`} class="text-xl font-bold">{title}</h2>
    {#if summary}<p class="text-muted-foreground ml-auto truncate text-sm">{summary}</p>{/if}
  </header>
  <div class="mt-5">{@render children?.()}</div>
</section>
