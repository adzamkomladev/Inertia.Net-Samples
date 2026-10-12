<script setup lang="ts">
import type { Kpis, SeriesPoint, TopPost } from '@/types'
import { Deferred, Head, Link } from '@inertiajs/vue3'
import { BarChart3 } from '@lucide/vue'
import { computed } from 'vue'
import EmptyState from '@/components/EmptyState.vue'
import EngagementChart from '@/components/analytics/EngagementChart.vue'
import KpiCards from '@/components/analytics/KpiCards.vue'
import RangeTabs from '@/components/analytics/RangeTabs.vue'
import ViewsChart from '@/components/analytics/ViewsChart.vue'
import PageHeader from '@/components/PageHeader.vue'
import { Button } from '@/components/ui/button'
import { Skeleton } from '@/components/ui/skeleton'

const props = defineProps<{ days: number; kpis?: Kpis; series?: SeriesPoint[]; topPosts?: TopPost[] }>()

const empty = computed(
  () =>
    !!props.kpis && !!props.series && !!props.topPosts &&
    props.kpis.views + props.kpis.likes + props.kpis.comments + props.kpis.posts === 0,
)
const max = computed(() => Math.max(1, ...(props.topPosts ?? []).map(p => p.views)))
</script>

<template>
  <Head title="Analytics" />
  <div class="flex w-full min-w-0 flex-col gap-6">
    <PageHeader title="Analytics" description="How your posts are doing.">
      <template #actions><RangeTabs :days="days" url="/analytics" /></template>
    </PageHeader>

    <EmptyState v-if="empty" :icon="BarChart3" title="No stats yet" description="Publish a post and share it to see stats here.">
      <Button as-child class="rounded-full"><Link href="/posts/create">Write your first post</Link></Button>
    </EmptyState>

    <template v-else>
      <Deferred data="kpis">
        <template #fallback>
          <div class="grid grid-cols-2 gap-px overflow-hidden rounded-xl border bg-border *:bg-card lg:grid-cols-5">
            <div v-for="i in 5" :key="i" class="space-y-2.5 p-4 sm:p-5">
              <Skeleton class="h-4 w-16" />
              <Skeleton class="h-8 w-24" />
            </div>
          </div>
        </template>
        <KpiCards v-if="kpis" :kpis="kpis" />
      </Deferred>

      <Deferred data="series">
        <template #fallback>
          <div class="grid gap-6 lg:grid-cols-2">
            <Skeleton class="h-[26rem] rounded-xl" />
            <Skeleton class="h-[26rem] rounded-xl" />
          </div>
        </template>
        <div v-if="series" class="grid gap-6 lg:grid-cols-2">
          <ViewsChart :series="series" />
          <EngagementChart :series="series" />
        </div>
      </Deferred>

      <Deferred data="topPosts">
        <template #fallback><Skeleton class="h-64 rounded-xl" /></template>
        <section v-if="topPosts" class="rounded-xl border bg-card">
          <header class="px-5 pt-5 pb-3">
            <h2 class="text-base font-semibold">Top posts</h2>
            <p class="text-sm text-muted-foreground">Ranked by views in this period.</p>
          </header>
          <p v-if="!topPosts.length" class="border-t px-5 py-8 text-center text-sm text-muted-foreground">No posts yet.</p>
          <ol v-else class="divide-y border-t">
            <li
              v-for="(p, i) in topPosts"
              :key="p.id"
              class="grid grid-cols-[1.5rem_1fr_auto] items-center gap-x-3 px-5 py-3 transition-colors hover:bg-muted/50 sm:grid-cols-[1.5rem_1fr_9rem_3.5rem_3.5rem]"
            >
              <span class="text-sm tabular-nums text-muted-foreground">{{ i + 1 }}</span>
              <Link :href="`/analytics/posts/${p.id}`" class="min-w-0 truncate font-serif text-[17px] font-medium underline-offset-4 hover:underline">{{ p.title }}</Link>
              <div class="flex items-center justify-end gap-2.5">
                <span class="hidden h-1 flex-1 overflow-hidden rounded-full bg-muted sm:block" aria-hidden="true">
                  <span class="block h-full rounded-full bg-chart-1" :style="{ width: `${(p.views / max) * 100}%` }" />
                </span>
                <span class="w-12 text-right text-sm font-medium tabular-nums">{{ p.views.toLocaleString() }}<span class="sr-only"> views</span></span>
              </div>
              <span class="hidden text-right text-sm tabular-nums text-muted-foreground sm:block">{{ p.likes.toLocaleString() }}<span class="sr-only"> likes</span></span>
              <span class="hidden text-right text-sm tabular-nums text-muted-foreground sm:block">{{ p.comments.toLocaleString() }}<span class="sr-only"> comments</span></span>
            </li>
          </ol>
        </section>
      </Deferred>
    </template>
  </div>
</template>