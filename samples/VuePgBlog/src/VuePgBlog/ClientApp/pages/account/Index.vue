<script setup lang="ts">
import { Head, Link } from '@inertiajs/vue3'
import { ArrowRight, Bell, CreditCard, Package, ShieldCheck, Truck, Wrench } from '@lucide/vue'
import PageHeader from '@/components/PageHeader.vue'
import StatusBadge from '@/components/StatusBadge.vue'
import StatCard from '@/components/StatCard.vue'
import PriceTag from '@/components/PriceTag.vue'
import { money } from '@/lib/money'
import { fmtDate } from '@/lib/time'
import type { AlertRow, NextInstallment, OpenDelivery, RecentOrder } from '@/types'

defineProps<{
  stats: { orders: number; activePlans: number; activeWarranties: number; openRepairs: number }
  recentOrders: RecentOrder[]
  nextInstallment: NextInstallment | null
  openDeliveries: OpenDelivery[]
  unreadAlerts: AlertRow[]
}>()
</script>

<template>
  <Head title="My account" />
    <Head title="My account" />

  <div class="flex flex-col gap-6">
    <PageHeader title="Welcome back" description="Everything you own, owe or are waiting on — in one place." />

    <div class="grid gap-4 sm:grid-cols-2 xl:grid-cols-4">
      <Link href="/account/orders" class="block">
        <StatCard :label="'Orders'" :value="stats.orders" :icon="Package" hint="all time" />
      </Link>
      <Link href="/account/credit" class="block">
        <StatCard :label="'Pay plans'" :value="stats.activePlans" :icon="CreditCard" hint="active installments" />
      </Link>
      <Link href="/account/care/warranties" class="block">
        <StatCard label="Warranties" :value="stats.activeWarranties" :icon="ShieldCheck" hint="active coverage" />
      </Link>
      <Link href="/account/care/repairs" class="block">
        <StatCard :label="'Open repairs'" :value="stats.openRepairs" :icon="Wrench" hint="in the workshop" />
      </Link>
    </div>

    <div class="grid gap-5 lg:grid-cols-[1fr_22rem]">
      <section class="bg-card shadow-card rounded-2xl border p-5">
        <div class="flex items-center justify-between">
          <h2 class="font-display flex items-center gap-2 text-base font-semibold"><Package class="size-4" /> Recent orders</h2>
          <Link href="/account/orders" class="text-muted-foreground hover:text-foreground inline-flex items-center gap-1 text-xs">
            All orders <ArrowRight class="size-3" />
          </Link>
        </div>
        <ul v-if="recentOrders.length" class="mt-3 divide-y">
          <li v-for="o in recentOrders" :key="o.id" class="flex flex-wrap items-center gap-3 py-3 first:pt-0">
            <Link :href="`/account/orders/${o.number}`" class="font-mono text-sm font-medium hover:underline">{{ o.number }}</Link>
            <StatusBadge :status="o.status" kind="order" size="xs" />
            <span class="text-muted-foreground ml-auto text-xs">{{ fmtDate(o.createdAt, true) }}</span>
            <PriceTag :price="o.total" size="sm" />
          </li>
        </ul>
        <p v-else class="text-muted-foreground mt-4 text-sm">No orders yet — the catalog is waiting.</p>
      </section>

      <aside class="flex flex-col gap-5">
        <div class="bg-card shadow-card rounded-2xl border p-5">
          <h2 class="font-display flex items-center gap-2 text-base font-semibold"><Bell class="size-4" /> Alerts</h2>
          <ul v-if="unreadAlerts.length" class="mt-2 divide-y">
            <li v-for="a in unreadAlerts.slice(0, 3)" :key="a.id" class="py-2 first:pt-0">
              <p class="truncate text-sm font-medium">{{ a.title }}</p>
              <p class="text-muted-foreground truncate text-xs">{{ a.body }}</p>
            </li>
          </ul>
          <p class="text-muted-foreground mt-2 text-xs">
            <Link href="/account/alerts" class="text-primary hover:underline">Open the inbox</Link>
          </p>
        </div>

        <div v-if="nextInstallment" class="bg-card shadow-card rounded-2xl border p-5">
          <h2 class="font-display flex items-center gap-2 text-base font-semibold"><CreditCard class="size-4" /> Next installment</h2>
          <p class="mt-2 text-lg font-semibold">{{ money(nextInstallment.amount) }}</p>
          <p class="text-muted-foreground text-xs">Due {{ fmtDate(nextInstallment.dueDate, true) }}{{ nextInstallment.plan ? ` · ${nextInstallment.plan}` : '' }}</p>
          <Link href="/account/credit" class="text-primary mt-3 inline-flex items-center gap-1 text-xs hover:underline">
            Pay now <ArrowRight class="size-3" />
          </Link>
        </div>

        <div class="bg-card shadow-card rounded-2xl border p-5">
          <h2 class="font-display flex items-center gap-2 text-base font-semibold"><Truck class="size-4" /> On the way</h2>
          <ul v-if="openDeliveries.length" class="mt-2 space-y-2">
            <li v-for="d in openDeliveries" :key="d.id" class="flex items-center justify-between gap-2 text-sm">
              <Link :href="`/orders/${d.orderNumber}`" class="font-mono hover:underline">{{ d.orderNumber }}</Link>
              <StatusBadge :status="d.status" kind="delivery" size="xs" />
              <span class="text-muted-foreground truncate text-xs">{{ d.rider ?? 'awaiting rider' }}</span>
            </li>
          </ul>
          <p v-else class="text-muted-foreground mt-2 text-sm">Nothing in transit right now.</p>
        </div>
      </aside>
    </div>
  </div>
</template>
