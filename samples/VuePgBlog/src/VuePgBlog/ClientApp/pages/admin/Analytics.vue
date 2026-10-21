<script setup lang="ts">
import { Head, Deferred, router } from '@inertiajs/vue3'
import { CreditCard, TrendingUp } from '@lucide/vue'
import PageHeader from '@/components/PageHeader.vue'
import StatCard from '@/components/StatCard.vue'
import { Button } from '@/components/ui/button'
import Skeleton from '@/components/ui/skeleton/Skeleton.vue'
import { BarList, RevenueChart } from '@/components/analytics'
import { moneyWhole } from '@/lib/money'
import type { AnalyticsKpis, CategoryShareRow, SeriesRow, TopProductRow } from '@/types'

const props = defineProps<{
  range: number
  kpis: AnalyticsKpis | null
  series: SeriesRow[] | null
  topProducts: TopProductRow[] | null
  categoryShare: CategoryShareRow[] | null
}>()

const setRange = (days: number) =>
  router.get('/admin/analytics', days === 30 ? {} : { range: days }, { preserveScroll: true, preserveState: true })
</script>

<template>
  <Head title="Sales analytics" />
    <Head title="Sales analytics" />

  <div class="flex flex-col gap-6">
    <PageHeader title="Sales analytics" description="Revenue, BNPL share and what actually sells — cached server-side for 60s per range (Inertia.Net 1.4 cached props).">
      <template #actions>
        <Button v-for="r in [7, 30, 90]" :key="r" :variant="props.range === r ? 'default' : 'outline'" size="sm" class="rounded-full" @click="setRange(r)">
          {{ r }} days
        </Button>
      </template>
    </PageHeader>

    <Deferred :data="['kpis', 'series', 'topProducts', 'categoryShare']">
      <template #fallback>
        <div class="grid gap-4 sm:grid-cols-2 xl:grid-cols-4">
          <Skeleton v-for="i in 4" :key="i" class="h-28 rounded-2xl" />
        </div>
        <Skeleton class="mt-5 h-72 rounded-2xl" />
      </template>
      <template #default>
        <div v-if="props.kpis" class="flex flex-col gap-5">
          <div class="grid gap-4 sm:grid-cols-2 xl:grid-cols-4">
            <StatCard label="Revenue" :value="moneyWhole(props.kpis.revenue)" :icon="TrendingUp" :hint="`${props.kpis.orders} paid orders`" />
            <StatCard label="BNPL share" :value="moneyWhole(props.kpis.bnplShare)" :icon="CreditCard" hint="financed volume" />
            <StatCard label="Pay on delivery" :value="props.kpis.pickups" hint="of paid orders" />
            <StatCard label="Delivered" :value="props.kpis.deliveries" hint="+ product views" />
          </div>

          <section class="bg-card shadow-card rounded-2xl border p-5">
            <h2 class="font-display text-base font-semibold">Revenue & BNPL</h2>
            <RevenueChart :series="props.series ?? []" class="mt-4" />
          </section>

          <div class="grid gap-5 lg:grid-cols-2">
            <section class="bg-card shadow-card rounded-2xl border p-5">
              <h2 class="font-display text-base font-semibold">Top products</h2>
              <BarList v-if="props.topProducts" :rows="props.topProducts.map((p) => ({ label: p.name, value: p.revenue }))" class="mt-4" />
              <p v-else class="text-muted-foreground mt-4 text-sm">No sales in this window.</p>
            </section>
            <section class="bg-card shadow-card rounded-2xl border p-5">
              <h2 class="font-display text-base font-semibold">Category share</h2>
              <BarList v-if="props.categoryShare" :rows="props.categoryShare.map((c) => ({ label: c.label, value: c.revenue }))" class="mt-4" />
              <p v-else class="text-muted-foreground mt-4 text-sm">No sales in this window.</p>
            </section>
          </div>

          <p class="text-muted-foreground text-xs">
            Deferred props are cached per range for 60 seconds — the first admin pays the query cost, everyone else in that
            minute rides free.
          </p>
        </div>
      </template>
    </Deferred>
  </div>
</template>
