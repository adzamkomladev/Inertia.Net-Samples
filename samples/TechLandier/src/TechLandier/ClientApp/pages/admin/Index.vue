<script setup lang="ts">
import { Head, Deferred, router } from '@inertiajs/vue3'
import { AlertTriangle, CreditCard, PackageX, PackageSearch, ShoppingCart, Timer, TrendingUp, Truck, Users } from '@lucide/vue'
import PageHeader from '@/components/PageHeader.vue'
import StatCard from '@/components/StatCard.vue'
import { Button } from '@/components/ui/button'
import Skeleton from '@/components/ui/skeleton/Skeleton.vue'
import { RevenueChart } from '@/components/analytics'
import { moneyWhole } from '@/lib/money'
import type { AdminKpis } from '@/types'

const props = defineProps<{ range: number; kpis: AdminKpis | null }>()

const setRange = (days: number) => router.get('/admin', { range: days }, { preserveScroll: true, preserveState: true })
</script>

<template>
  <Head title="Admin" />
    <Head title="Admin" />

  <div class="flex flex-col gap-6">
    <PageHeader title="Store pulse" description="Revenue, stock pressure and everything asking for a decision.">
      <template #actions>
        <Button v-for="r in [7, 30]" :key="r" :variant="range === r ? 'default' : 'outline'" size="sm" class="rounded-full" @click="setRange(r)">
          {{ r }} days
        </Button>
      </template>
    </PageHeader>

    <Deferred :data="['kpis']">
      <template #fallback>
        <div class="grid gap-4 sm:grid-cols-2 xl:grid-cols-4">
          <Skeleton v-for="i in 4" :key="i" class="h-28 rounded-2xl" />
        </div>
        <Skeleton class="mt-5 h-72 rounded-2xl" />
      </template>

      <template #default>
        <div v-if="props.kpis" class="flex flex-col gap-5">
        <div class="grid gap-4 sm:grid-cols-2 xl:grid-cols-4">
          <StatCard label="Revenue" :value="moneyWhole(props.kpis.revenue)" :icon="TrendingUp" :hint="`${range} days, paid orders`" />
          <StatCard label="Orders" :value="props.kpis.orders" :icon="ShoppingCart" :hint="`AOV ${moneyWhole(props.kpis.aov)}`" />
          <StatCard label="BNPL share" :value="moneyWhole(props.kpis.planExposure)" :icon="CreditCard" hint="financed volume" />
          <StatCard label="Low stock" :value="props.kpis.attention.lowStockCount" :icon="PackageX" hint="at or below threshold" />
        </div>

        <!-- needs attention -->
        <div class="bg-card shadow-card mt-1 rounded-2xl border p-5">
          <h2 class="font-display flex items-center gap-2 text-base font-semibold">
            <AlertTriangle class="size-4" /> Needs attention
          </h2>
          <div class="mt-4 grid gap-3 sm:grid-cols-2 xl:grid-cols-4">
            <a
              v-for="row in [
                { label: 'Orders to confirm', count: props.kpis.attention.ordersToConfirm, href: '/admin/orders', icon: ShoppingCart },
                { label: 'Deliveries to assign', count: props.kpis.attention.deliveriesToAssign, href: '/admin/deliveries', icon: Truck },
                { label: 'Overdue tickets', count: props.kpis.attention.overdueTickets, href: '/admin/support', icon: Timer },
                { label: 'Care claims waiting', count: props.kpis.attention.warrantyClaims + props.kpis.attention.insuranceClaims, href: '/admin/care', icon: Users },
              ]"
              :key="row.label"
              :href="row.href"
              class="group hover:border-primary/40 flex items-center gap-3 rounded-xl border p-3 transition-colors"
            >
              <component :is="row.icon" class="text-muted-foreground size-4" />
              <span class="text-sm">{{ row.label }}</span>
              <span
                class="ml-auto rounded-full px-2 py-0.5 text-xs font-semibold tabular-nums"
                :class="row.count > 0 ? 'bg-cta/15 text-cta-foreground' : 'bg-muted text-muted-foreground'"
              >
                {{ row.count }}
              </span>
            </a>
          </div>
        </div>

        <!-- revenue chart -->
        <section class="bg-card shadow-card mt-5 rounded-2xl border p-5">
          <h2 class="font-display text-base font-semibold">Revenue</h2>
          <RevenueChart :series="props.kpis.revenueSeries" class="mt-4" />
        </section>

        <!-- low stock -->
        <section v-if="props.kpis.lowStock.length" class="bg-card shadow-card rounded-2xl border p-5">
          <h2 class="font-display flex items-center gap-2 text-base font-semibold">
            <PackageSearch class="size-4" /> Restock queue
          </h2>
          <ul class="mt-3 divide-y">
            <li v-for="p in props.kpis?.lowStock" :key="p.id" class="flex items-center gap-3 py-2 text-sm first:pt-0 last:pb-0">
              <span class="min-w-0 flex-1 truncate font-medium">{{ p.name }}</span>
              <span class="text-muted-foreground">{{ p.brand }}</span>
              <span class="bg-warning/15 border-warning/30 text-warning rounded-full border px-2 py-0.5 text-xs tabular-nums">
                {{ p.stock }} / threshold {{ p.lowStockThreshold }}
              </span>
            </li>
          </ul>
        </section>
        </div>
      </template>
    </Deferred>
  </div>
</template>
