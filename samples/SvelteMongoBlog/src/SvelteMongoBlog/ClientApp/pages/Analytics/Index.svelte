<script lang="ts">
  import { Deferred, Link } from '@inertiajs/svelte'
  import { BarChart3 } from '@lucide/svelte'
  import EmptyState from '@/components/EmptyState.svelte'
  import PageHeader from '@/components/PageHeader.svelte'
  import PageTitle from '@/components/PageTitle.svelte'
  import EngagementChart from '@/components/analytics/EngagementChart.svelte'
  import KpiCards from '@/components/analytics/KpiCards.svelte'
  import RangeTabs from '@/components/analytics/RangeTabs.svelte'
  import ViewsChart from '@/components/analytics/ViewsChart.svelte'
  import Button from '@/components/ui/button/Button.svelte'
  import Skeleton from '@/components/ui/skeleton/Skeleton.svelte'
  import type { Kpis, SeriesPoint, TopPost } from '@/types'

  let { days, kpis, series, topPosts }: { days: number; kpis?: Kpis; series?: SeriesPoint[]; topPosts?: TopPost[] } = $props()

  const empty = $derived(
    !!kpis && !!series && !!topPosts &&
    kpis.views + kpis.likes + kpis.comments + kpis.posts === 0,
  )
  const max = $derived(Math.max(1, ...(topPosts ?? []).map(p => p.views)))
</script>

<PageTitle title="Analytics" />
<div class="flex w-full min-w-0 flex-col gap-6">
  <PageHeader title="Analytics" description="How your posts are doing.">
    {#snippet actions()}<RangeTabs {days} url="/analytics" />{/snippet}
  </PageHeader>

  {#if empty}
    <EmptyState icon={BarChart3} title="No stats yet" description="Publish a post and share it to see stats here.">
      <Button href="/posts/create" class="rounded-full">Write your first post</Button>
    </EmptyState>
  {:else}
    <Deferred data="kpis">
      {#snippet fallback()}
        <div class="grid grid-cols-2 gap-px overflow-hidden rounded-xl border bg-border *:bg-card lg:grid-cols-5">
          {#each [0, 1, 2, 3, 4] as i (i)}
            <div class="space-y-2.5 p-4 sm:p-5">
              <Skeleton class="h-4 w-16" />
              <Skeleton class="h-8 w-24" />
            </div>
          {/each}
        </div>
      {/snippet}
      {#if kpis}<KpiCards {kpis} />{/if}
    </Deferred>

    <Deferred data="series">
      {#snippet fallback()}
        <div class="grid gap-6 lg:grid-cols-2">
          <Skeleton class="h-[26rem] rounded-xl" />
          <Skeleton class="h-[26rem] rounded-xl" />
        </div>
      {/snippet}
      {#if series}
        <div class="grid gap-6 lg:grid-cols-2">
          <ViewsChart {series} />
          <EngagementChart {series} />
        </div>
      {/if}
    </Deferred>

    <Deferred data="topPosts">
      {#snippet fallback()}<Skeleton class="h-64 rounded-xl" />{/snippet}
      {#if topPosts}
        <section class="rounded-xl border bg-card">
          <header class="px-5 pt-5 pb-3">
            <h2 class="text-base font-semibold">Top posts</h2>
            <p class="text-muted-foreground text-sm">Ranked by views in this period.</p>
          </header>
          {#if !topPosts.length}
            <p class="text-muted-foreground border-t px-5 py-8 text-center text-sm">No posts yet.</p>
          {:else}
            <ol class="divide-y border-t">
              {#each topPosts as p, i (p.id)}
                <li class="hover:bg-muted/50 grid grid-cols-[1.5rem_1fr_auto] items-center gap-x-3 px-5 py-3 transition-colors sm:grid-cols-[1.5rem_1fr_9rem_3.5rem_3.5rem]">
                  <span class="text-muted-foreground text-sm tabular-nums">{i + 1}</span>
                  <Link href={`/analytics/posts/${p.id}`} class="min-w-0 truncate font-serif text-[17px] font-medium underline-offset-4 hover:underline">{p.title}</Link>
                  <div class="flex items-center justify-end gap-2.5">
                    <span class="bg-muted hidden h-1 flex-1 overflow-hidden rounded-full sm:block" aria-hidden="true">
                      <span class="bg-chart-1 block h-full rounded-full" style={`width:${(p.views / max) * 100}%`}></span>
                    </span>
                    <span class="w-12 text-right text-sm font-medium tabular-nums">{p.views.toLocaleString()}<span class="sr-only"> views</span></span>
                  </div>
                  <span class="text-muted-foreground hidden text-right text-sm tabular-nums sm:block">{p.likes.toLocaleString()}<span class="sr-only"> likes</span></span>
                  <span class="text-muted-foreground hidden text-right text-sm tabular-nums sm:block">{p.comments.toLocaleString()}<span class="sr-only"> comments</span></span>
                </li>
              {/each}
            </ol>
          {/if}
        </section>
      {/if}
    </Deferred>
  {/if}
</div>
