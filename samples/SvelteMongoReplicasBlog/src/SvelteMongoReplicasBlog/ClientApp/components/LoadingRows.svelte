<script lang="ts">
  import Skeleton from '@/components/ui/skeleton/Skeleton.svelte'

  let { kind }: { kind: 'posts' | 'users' } = $props()
</script>

<div role="status" aria-label="Loading more" class="animate-in fade-in duration-300">
  {#each [0, 1] as i (i)}
    {#if kind === 'users'}
      <div class="flex items-center gap-3 border-b py-4" style={`opacity:${1 - i * 0.4}`}>
        <Skeleton class="size-11 shrink-0 rounded-full" />
        <div class="flex-1 space-y-2"><Skeleton class="h-3.5 w-36" /><Skeleton class="h-3 w-24" /></div>
        <Skeleton class="h-8 w-24 rounded-full" />
      </div>
    {:else}
      <div class="flex gap-4 border-b py-5" style={`opacity:${1 - i * 0.4}`}>
        <Skeleton class="size-10 shrink-0 rounded-full" />
        <div class="flex-1 space-y-3">
          <Skeleton class="h-3.5 w-40" />
          <Skeleton class="h-5 w-4/5" />
          <div class="space-y-2"><Skeleton class="h-3.5 w-full" /><Skeleton class="h-3.5 w-2/3" /></div>
        </div>
      </div>
    {/if}
  {/each}
</div>
