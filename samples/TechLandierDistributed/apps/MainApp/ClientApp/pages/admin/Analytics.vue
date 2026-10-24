<script setup lang="ts">
import { computed } from 'vue'
import { Deferred, Head, router } from '@inertiajs/vue3'
import { CreditCard, TrendingUp } from '@lucide/vue'
import PageHeader from '@/components/PageHeader.vue'
import SegmentedControl from '@/components/SegmentedControl.vue'
import StatCard from '@/components/StatCard.vue'
import { Skeleton } from '@/components/ui/skeleton'
import { BarList, RevenueChart } from '@/components/analytics'
import { moneyWhole } from '@/lib/money'
import type { AnalyticsKpis, CategoryShareRow, SeriesRow, TopProductRow } from '@/types'

const props = defineProps<{
  range: number
  kpis?: AnalyticsKpis | null
  series?: SeriesRow[] | null
  topProducts?: TopProductRow[] | null
  categoryShare?: CategoryShareRow[] | null
}>()

const rangeModel = computed({
  get: () => props.range,
  set: (days) => router.get('/admin/analytics', days === 30 ? {} : { range: days }, { preserveScroll: true, preserveState: true }),
})
</script>

<template>
  <Head title="Analytics" />

  <div class="flex flex-col gap-8">
    <PageHeader title="Analytics" description="Revenue, financed volume and what actually sells.">
      <template #actions>
        <SegmentedControl v-model="rangeModel" label="Date range" :options="[{ value: 7, label: '7 days' }, { value: 30, label: '30 days' }, { value: 90, label: '90 days' }]" />
      </template>
    </PageHeader>

    <Deferred :data="['kpis', 'series', 'topProducts', 'categoryShare']">
      <template #fallback>
        <div class="space-y-8" aria-busy="true">
          <div class="grid gap-4 sm:grid-cols-2 xl:grid-cols-4"><Skeleton v-for="i in 4" :key="i" class="h-28 rounded-xl" /></div>
          <Skeleton class="h-80 rounded-xl" />
          <div class="grid gap-5 lg:grid-cols-2"><Skeleton v-for="i in 2" :key="i" class="h-64 rounded-xl" /></div>
        </div>
      </template>

      <div v-if="kpis" class="flex flex-col gap-8">
        <div class="grid gap-4 sm:grid-cols-2 xl:grid-cols-4">
          <StatCard label="Revenue" :value="moneyWhole(kpis.revenue)" :icon="TrendingUp" :hint="`${kpis.orders} paid orders`" />
          <StatCard label="Financed" :value="moneyWhole(kpis.bnplShare)" :icon="CreditCard" hint="Value on installment plans" />
          <StatCard label="Pay on delivery" :value="kpis.pickups" hint="Cash orders" />
          <StatCard label="Delivered" :value="kpis.deliveries" hint="Orders in this range" />
        </div>

        <section class="surface p-5" aria-labelledby="rev-h">
          <h2 id="rev-h" class="text-base font-semibold">Revenue</h2>
          <RevenueChart :series="series ?? []" class="mt-4" />
        </section>

        <div class="grid gap-5 lg:grid-cols-2">
          <section class="surface p-5" aria-labelledby="top-h">
            <h2 id="top-h" class="text-base font-semibold">Top products</h2>
            <BarList v-if="topProducts?.length" :rows="topProducts.map((p) => ({ label: p.name, value: p.revenue }))" :format="moneyWhole" class="mt-4" />
            <p v-else class="text-muted-foreground mt-4 text-sm">No sales in this range.</p>
          </section>
          <section class="surface p-5" aria-labelledby="cat-h">
            <h2 id="cat-h" class="text-base font-semibold">Category share</h2>
            <BarList v-if="categoryShare?.length" :rows="categoryShare.map((c) => ({ label: c.label, value: c.revenue }))" :format="moneyWhole" class="mt-4" />
            <p v-else class="text-muted-foreground mt-4 text-sm">No sales in this range.</p>
          </section>
        </div>
      </div>
    </Deferred>
  </div>
</template>
