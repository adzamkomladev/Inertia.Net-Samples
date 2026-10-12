<script lang="ts">
  import { Deferred, Link } from '@inertiajs/svelte'
  import { ArrowLeft } from '@lucide/svelte'
  import PageHeader from '@/components/PageHeader.svelte'
  import PageTitle from '@/components/PageTitle.svelte'
  import KpiCards from '@/components/analytics/KpiCards.svelte'
  import RangeTabs from '@/components/analytics/RangeTabs.svelte'
  import ViewsChart from '@/components/analytics/ViewsChart.svelte'
  import Skeleton from '@/components/ui/skeleton/Skeleton.svelte'
  import type { Kpis, Referrer, SeriesPoint } from '@/types'

  let {
    days,
    post,
    kpis,
    series,
    referrers,
  }: {
    days: number
    post: { id: string; title: string; slug: string }
    kpis?: Kpis
    series?: SeriesPoint[]
    referrers?: Referrer[]
  } = $props()

  const max = $derived(Math.max(1, ...(referrers ?? []).map(r => r.views)))
</script>

<PageTitle title={`Analytics: ${post.title}`} />
<div class="flex w-full min-w-0 flex-col gap-6">
  <Link href="/analytics" class="text-muted-foreground hover:text-foreground inline-flex w-fit items-center gap-1 text-sm transition-colors">
    <ArrowLeft class="size-4" aria-hidden="true" /> Analytics
  </Link>
  <PageHeader>
    {#snippet title()}<span class="font-serif">{post.title}</span>{/snippet}
    {#snippet description()}
      <Link href={`/posts/${post.slug}`} class="hover:text-foreground underline-offset-4 transition-colors hover:underline">View post</Link>
    {/snippet}
    {#snippet actions()}<RangeTabs {days} url={`/analytics/posts/${post.id}`} />{/snippet}
  </PageHeader>

  <Deferred data="kpis">
    {#snippet fallback()}
      <div class="grid grid-cols-2 gap-px overflow-hidden rounded-xl border bg-border *:bg-card lg:grid-cols-4">
        {#each [0, 1, 2, 3] as i (i)}
          <div class="space-y-2.5 p-4 sm:p-5">
            <Skeleton class="h-4 w-16" />
            <Skeleton class="h-8 w-24" />
          </div>
        {/each}
      </div>
    {/snippet}
    {#if kpis}<KpiCards {kpis} hidePosts />{/if}
  </Deferred>

  <Deferred data="series">
    {#snippet fallback()}<Skeleton class="h-[26rem] rounded-xl" />{/snippet}
    {#if series}<ViewsChart {series} />{/if}
  </Deferred>

  <Deferred data="referrers">
    {#snippet fallback()}<Skeleton class="h-48 rounded-xl" />{/snippet}
    {#if referrers}
      <section class="rounded-xl border bg-card">
        <header class="px-5 pt-5 pb-3">
          <h2 class="text-base font-semibold">Referrers</h2>
          <p class="text-muted-foreground text-sm">Where readers came from.</p>
        </header>
        {#if !referrers.length}
          <p class="text-muted-foreground border-t px-5 py-8 text-center text-sm">No referrer data yet.</p>
        {:else}
          <ul class="divide-y border-t">
            {#each referrers as r (r.host)}
              <li class="hover:bg-muted/50 flex flex-col gap-1.5 px-5 py-3 transition-colors">
                <div class="flex justify-between gap-3 text-sm">
                  <span class="truncate">{r.host || 'Direct'}</span>
                  <span class="font-medium tabular-nums">{r.views.toLocaleString()}</span>
                </div>
                <div class="bg-muted h-1 overflow-hidden rounded-full" aria-hidden="true">
                  <div class="bg-chart-1 h-full rounded-full" style={`width:${(r.views / max) * 100}%`}></div>
                </div>
              </li>
            {/each}
          </ul>
        {/if}
      </section>
    {/if}
  </Deferred>
</div>
