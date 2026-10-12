<script lang="ts">
  import { router } from '@inertiajs/svelte'
  import { cn } from '@/lib/utils'

  let { days, url }: { days: number; url: string } = $props()

  const setDays = (value: number) =>
    router.get(url, { days: value }, { preserveState: true, preserveScroll: true, replace: true })
</script>

<div role="tablist" aria-label="Time range" class="bg-muted inline-flex h-auto rounded-lg p-0.5">
  {#each [7, 30, 90] as value (value)}
    <button
      type="button" role="tab" aria-selected={days === value}
      class={cn('rounded-md px-3 py-1 text-[13px] transition-colors', days === value && 'bg-background shadow-xs')}
      onclick={() => setDays(value)}
    >{value}d</button>
  {/each}
</div>
