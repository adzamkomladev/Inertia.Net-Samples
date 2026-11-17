<script lang="ts">
  import Skeleton from '@/components/ui/skeleton/Skeleton.svelte'
  import type { PendingKind } from '@/lib/pending-visit.svelte'

  /** The shape of the page a visitor is heading to, so a tap answers on the click and the old page never looks stuck. */
  let { kind }: { kind: PendingKind } = $props()
</script>

<div role="status" aria-label="Loading page" class="animate-in fade-in duration-100">
  {#if kind === 'lots'}
    <div class="mx-auto w-full max-w-6xl px-4 py-8 sm:px-6">
      <Skeleton class="h-9 w-56" />
      <Skeleton class="mt-3 h-4 w-72 max-w-full" />
      <Skeleton class="mt-6 h-11 w-full rounded-xl" />
      <div class="mt-8 grid grid-cols-1 gap-4 sm:grid-cols-2 lg:grid-cols-3">
        {#each Array(6) as _, i (i)}<Skeleton class="h-40 rounded-xl" />{/each}
      </div>
    </div>
  {:else if kind === 'lot'}
    <div class="mx-auto w-full max-w-6xl px-4 pt-6 sm:px-6">
      <Skeleton class="h-5 w-24" />
      <Skeleton class="mt-4 h-10 w-2/3" />
      <Skeleton class="mt-3 h-4 w-80 max-w-full" />
      <div class="mt-8 grid items-start gap-6 lg:grid-cols-[1fr_20rem]">
        <Skeleton class="h-80 rounded-xl" />
        <Skeleton class="h-56 rounded-xl" />
      </div>
    </div>
  {:else if kind === 'book'}
    <div class="mx-auto w-full max-w-6xl px-4 pt-6 sm:px-6">
      <Skeleton class="h-5 w-32" />
      <Skeleton class="mt-4 h-9 w-52" />
      <div class="mt-6 grid items-start gap-6 lg:grid-cols-[1fr_22rem]">
        <div class="flex flex-col gap-4"><Skeleton class="h-72 rounded-xl" /><Skeleton class="h-64 rounded-xl" /></div>
        <Skeleton class="hidden h-80 rounded-xl lg:block" />
      </div>
    </div>
  {:else if kind === 'dashboard'}
    <div class="space-y-6">
      <div class="space-y-2"><Skeleton class="h-8 w-48" /><Skeleton class="h-4 w-72 max-w-full" /></div>
      <div class="grid grid-cols-2 gap-3 xl:grid-cols-4">{#each Array(4) as _, i (i)}<Skeleton class="h-[7.25rem] rounded-xl" />{/each}</div>
      <Skeleton class="h-64 rounded-xl" />
    </div>
  {:else if kind === 'guard'}
    <div class="grid items-start gap-6 lg:grid-cols-[minmax(0,25rem)_1fr]">
      <Skeleton class="h-96 rounded-xl" />
      <Skeleton class="h-96 rounded-xl" />
    </div>
  {:else}
    <div class="space-y-6">
      <div class="space-y-2"><Skeleton class="h-8 w-48" /><Skeleton class="h-4 w-72 max-w-full" /></div>
      <div class="bg-card shadow-card divide-y rounded-xl">
        {#each Array(6) as _, i (i)}<div class="space-y-2 p-4"><Skeleton class="h-4 w-1/3" /><Skeleton class="h-3 w-2/3" /></div>{/each}
      </div>
    </div>
  {/if}
</div>
