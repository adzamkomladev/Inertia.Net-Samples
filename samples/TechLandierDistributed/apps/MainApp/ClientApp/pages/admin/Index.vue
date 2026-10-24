<script setup lang="ts">
import { computed } from 'vue'
import { Deferred, Head, Link, router } from '@inertiajs/vue3'
import { ChevronRight, CreditCard, PackageX, ShoppingCart, TrendingUp } from '@lucide/vue'
import PageHeader from '@/components/PageHeader.vue'
import SegmentedControl from '@/components/SegmentedControl.vue'
import StatCard from '@/components/StatCard.vue'
import { Skeleton } from '@/components/ui/skeleton'
import { RevenueChart } from '@/components/analytics'
import { moneyWhole } from '@/lib/money'
import type { AdminKpis, Attention, LowStockRow, RevenueSeriesPoint } from '@/types'

const props = defineProps<{
  range: number
  kpis?: AdminKpis | null
  revenueSeries?: RevenueSeriesPoint[] | null
  lowStock?: LowStockRow[] | null
  attention?: Attention | null
}>()

const rangeModel = computed({
  get: () => props.range,
  set: (days) => router.get('/admin', { range: days }, { preserveScroll: true, preserveState: true }),
})

const queue = computed(() => {
  const a = props.attention
  if (!a) return []
  return [
    { label: 'Orders to confirm', count: a.ordersToConfirm, href: '/admin/orders' },
    { label: 'Deliveries without a rider', count: a.deliveriesToAssign, href: '/admin/deliveries' },
    { label: 'Support tickets past due', count: a.overdueTickets, href: '/admin/support' },
    { label: 'Warranty and insurance claims', count: a.warrantyClaims + a.insuranceClaims, href: '/admin/care' },
  ]
})
</script>

<template>
  <Head title="Overview" />

  <div class="flex flex-col gap-8">
    <PageHeader title="Overview" description="What needs a decision today, then how the store is doing.">
      <template #actions>
        <SegmentedControl v-model="rangeModel" label="Date range" :options="[{ value: 7, label: '7 days' }, { value: 30, label: '30 days' }]" />
      </template>
    </PageHeader>

    <Deferred :data="['kpis', 'revenueSeries', 'lowStock', 'attention']">
      <template #fallback>
        <div class="space-y-8" aria-busy="true">
          <Skeleton class="h-56 rounded-xl" />
          <div class="grid gap-4 sm:grid-cols-2 xl:grid-cols-4"><Skeleton v-for="i in 4" :key="i" class="h-28 rounded-xl" /></div>
          <Skeleton class="h-80 rounded-xl" />
        </div>
      </template>

      <template v-if="kpis && attention">
        <div class="flex flex-col gap-8">
          <section class="surface overflow-hidden" aria-labelledby="queue-h">
            <h2 id="queue-h" class="px-5 pt-5 pb-2 text-base font-semibold">Needs attention</h2>
            <ul class="divide-y">
              <li v-for="row in queue" :key="row.label">
                <Link :href="row.href" prefetch class="hover:bg-muted/50 flex min-h-14 items-center gap-3 px-5 py-3 text-sm transition-colors">
                  <span :class="row.count > 0 ? 'text-foreground' : 'text-muted-foreground'">{{ row.label }}</span>
                  <span :class="['price ml-auto min-w-8 rounded-full px-2.5 py-0.5 text-center text-sm font-semibold', row.count > 0 ? 'bg-warning/15 text-warning' : 'bg-muted text-muted-foreground']">{{ row.count }}</span>
                  <ChevronRight class="text-muted-foreground size-4" aria-hidden="true" />
                </Link>
              </li>
            </ul>
          </section>

          <div class="grid gap-4 sm:grid-cols-2 xl:grid-cols-4">
            <StatCard label="Revenue" :value="moneyWhole(kpis.revenue)" :icon="TrendingUp" :hint="`Paid orders, last ${range} days`" />
            <StatCard label="Orders" :value="kpis.orders" :icon="ShoppingCart" :hint="`Average ${moneyWhole(kpis.aov)} each`" />
            <StatCard label="Financed" :value="moneyWhole(kpis.planExposure)" :icon="CreditCard" hint="Value on installment plans" />
            <StatCard label="Low stock" :value="attention.lowStockCount" :icon="PackageX" hint="At or below threshold" :tone="attention.lowStockCount > 0 ? 'warning' : 'default'" />
          </div>

          <section class="surface p-5" aria-labelledby="rev-h">
            <h2 id="rev-h" class="text-base font-semibold">Revenue</h2>
            <RevenueChart :series="revenueSeries ?? []" class="mt-4" />
          </section>

          <section v-if="lowStock?.length" class="surface overflow-hidden" aria-labelledby="stock-h">
            <div class="flex items-center justify-between px-5 pt-5 pb-2">
              <h2 id="stock-h" class="text-base font-semibold">Restock queue</h2>
              <Link href="/admin/catalog" prefetch class="text-primary text-sm font-medium hover:underline">Open catalog</Link>
            </div>
            <ul class="divide-y">
              <li v-for="p in lowStock" :key="p.id" class="flex flex-wrap items-center gap-x-4 gap-y-1 px-5 py-3 text-sm">
                <span class="min-w-0 flex-1 basis-48 truncate font-medium">{{ p.name }}</span>
                <span class="text-muted-foreground">{{ p.brand }}</span>
                <span class="text-warning price w-32 text-right font-medium">{{ p.stock }} left <span class="text-muted-foreground font-normal">of {{ p.lowStockThreshold }}</span></span>
              </li>
            </ul>
          </section>
        </div>
      </template>
    </Deferred>
  </div>
</template>
