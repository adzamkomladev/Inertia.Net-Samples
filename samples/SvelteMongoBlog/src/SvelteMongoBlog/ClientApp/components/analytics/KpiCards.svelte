<script lang="ts">
  import type { Kpis } from '@/types'

  let { kpis, hidePosts = false }: { kpis: Kpis; hidePosts?: boolean } = $props()

  const items = $derived([
    { label: 'Views', value: kpis.views },
    { label: 'Unique viewers', value: kpis.uniqueViewers },
    { label: 'Likes', value: kpis.likes },
    { label: 'Comments', value: kpis.comments },
    ...(hidePosts ? [] : [{ label: 'Posts', value: kpis.posts }]),
  ])
</script>

<div class={`grid gap-px overflow-hidden rounded-xl border bg-border *:bg-card ${hidePosts ? 'grid-cols-2 lg:grid-cols-4' : 'grid-cols-2 lg:grid-cols-5'}`}>
  {#each items as item, index (item.label)}
    <div class="animate-rise space-y-1.5 p-4 sm:p-5" style={`animation-delay:${index * 40}ms`}>
      <p class="text-muted-foreground text-sm">{item.label}</p>
      <p class="text-2xl font-semibold tabular-nums tracking-tight">{item.value.toLocaleString()}</p>
    </div>
  {/each}
</div>
