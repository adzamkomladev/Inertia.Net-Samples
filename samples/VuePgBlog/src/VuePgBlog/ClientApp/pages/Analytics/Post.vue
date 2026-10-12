<script setup lang="ts">
import type { Kpis, Referrer, SeriesPoint } from '@/types'
import { Deferred, Head, Link } from '@inertiajs/vue3'
import { ArrowLeft } from '@lucide/vue'
import { computed } from 'vue'
import KpiCards from '@/components/analytics/KpiCards.vue'
import RangeTabs from '@/components/analytics/RangeTabs.vue'
import ViewsChart from '@/components/analytics/ViewsChart.vue'
import PageHeader from '@/components/PageHeader.vue'
import { Skeleton } from '@/components/ui/skeleton'

const props = defineProps<{
  days: number
  post: { id: string; title: string; slug: string }
  kpis?: Kpis
  series?: SeriesPoint[]
  referrers?: Referrer[]
}>()

const max = computed(() => Math.max(1, ...(props.referrers ?? []).map(r => r.views)))
</script>

<template>
  <Head :title="`Analytics: ${post.title}`" />
  <div class="flex w-full min-w-0 flex-col gap-6">
    <Link href="/analytics" class="inline-flex w-fit items-center gap-1 text-sm text-muted-foreground transition-colors hover:text-foreground">
      <ArrowLeft class="size-4" aria-hidden="true" /> Analytics
    </Link>
    <PageHeader>
      <template #title><span class="font-serif">{{ post.title }}</span></template>
      <template #description>
        <Link :href="`/posts/${post.slug}`" class="underline-offset-4 transition-colors hover:text-foreground hover:underline">View post</Link>
      </template>
      <template #actions><RangeTabs :days="days" :url="`/analytics/posts/${post.id}`" /></template>
    </PageHeader>

    <Deferred data="kpis">
      <template #fallback>
        <div class="grid grid-cols-2 gap-px overflow-hidden rounded-xl border bg-border *:bg-card lg:grid-cols-4">
          <div v-for="i in 4" :key="i" class="space-y-2.5 p-4 sm:p-5">
            <Skeleton class="h-4 w-16" />
            <Skeleton class="h-8 w-24" />
          </div>
        </div>
      </template>
      <KpiCards v-if="kpis" :kpis="kpis" hide-posts />
    </Deferred>

    <Deferred data="series">
      <template #fallback><Skeleton class="h-[26rem] rounded-xl" /></template>
      <ViewsChart v-if="series" :series="series" />
    </Deferred>

    <Deferred data="referrers">
      <template #fallback><Skeleton class="h-48 rounded-xl" /></template>
      <section v-if="referrers" class="rounded-xl border bg-card">
        <header class="px-5 pt-5 pb-3">
          <h2 class="text-base font-semibold">Referrers</h2>
          <p class="text-sm text-muted-foreground">Where readers came from.</p>
        </header>
        <p v-if="!referrers.length" class="border-t px-5 py-8 text-center text-sm text-muted-foreground">No referrer data yet.</p>
        <ul v-else class="divide-y border-t">
          <li v-for="r in referrers" :key="r.host" class="flex flex-col gap-1.5 px-5 py-3 transition-colors hover:bg-muted/50">
            <div class="flex justify-between gap-3 text-sm">
              <span class="truncate">{{ r.host || 'Direct' }}</span>
              <span class="font-medium tabular-nums">{{ r.views.toLocaleString() }}</span>
            </div>
            <div class="h-1 overflow-hidden rounded-full bg-muted" aria-hidden="true">
              <div class="h-full rounded-full bg-chart-1" :style="{ width: `${(r.views / max) * 100}%` }" />
            </div>
          </li>
        </ul>
      </section>
    </Deferred>
  </div>
</template>