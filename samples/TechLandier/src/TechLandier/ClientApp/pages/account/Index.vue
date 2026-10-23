<script setup lang="ts">
import { computed } from 'vue'
import { Deferred, Head, Link, usePage } from '@inertiajs/vue3'
import { Bell, ChevronRight, CreditCard, PackageCheck, Truck, Wrench } from '@lucide/vue'
import EmptyState from '@/components/EmptyState.vue'
import PageHeader from '@/components/PageHeader.vue'
import PriceTag from '@/components/PriceTag.vue'
import StatusBadge from '@/components/StatusBadge.vue'
import { Button } from '@/components/ui/button'
import { Skeleton } from '@/components/ui/skeleton'
import { money } from '@/lib/money'
import { fmtDate } from '@/lib/time'
import type { AlertRow, NextInstallment, OpenDelivery, RecentOrder } from '@/types'

const props = defineProps<{
  stats: { orders: number; activePlans: number; activeWarranties: number; openRepairs: number }
  recentOrders: RecentOrder[]
  nextInstallment?: NextInstallment | null
  openDeliveries?: OpenDelivery[]
  unreadAlerts?: AlertRow[]
}>()

const firstName = computed(() => usePage().props.auth.user?.name.split(/\s+/)[0] ?? '')

/** The attention list only contains things that are actually waiting on the customer. */
const attention = computed(() => {
  const items: { key: string; icon: typeof Bell; title: string; text: string; href: string; cta: string }[] = []
  const n = props.nextInstallment
  if (n) items.push({ key: 'installment', icon: CreditCard, title: `${money(n.amount)} due ${fmtDate(n.dueDate, true)}`, text: n.plan ? `Installment on ${n.plan}` : 'Next installment', href: '/account/credit', cta: 'Pay' })
  for (const d of props.openDeliveries ?? []) items.push({ key: d.id, icon: Truck, title: `Order ${d.orderNumber} is on its way`, text: d.rider ? `Rider: ${d.rider}` : 'Waiting for a rider', href: `/orders/${d.orderNumber}`, cta: 'Track' })
  if (props.stats.openRepairs > 0) items.push({ key: 'repairs', icon: Wrench, title: `${props.stats.openRepairs} ${props.stats.openRepairs === 1 ? 'repair' : 'repairs'} in progress`, text: 'See quotes and status updates', href: '/account/care/repairs', cta: 'View' })
  const alerts = props.unreadAlerts ?? []
  if (alerts.length) items.push({ key: 'alerts', icon: Bell, title: `${alerts.length} unread ${alerts.length === 1 ? 'alert' : 'alerts'}`, text: alerts[0]!.title, href: '/account/alerts', cta: 'Open' })
  return items
})

const summary = computed(() => [
  { label: 'Orders', value: props.stats.orders, href: '/account/orders' },
  { label: 'Active plans', value: props.stats.activePlans, href: '/account/credit' },
  { label: 'Warranties', value: props.stats.activeWarranties, href: '/account/care/warranties' },
  { label: 'Open repairs', value: props.stats.openRepairs, href: '/account/care/repairs' },
])
</script>

<template>
  <Head title="My account" />

  <div class="flex flex-col gap-8">
    <PageHeader :title="`Hello, ${firstName}`" description="What is waiting on you, and where your orders are." />

    <Deferred :data="['nextInstallment', 'openDeliveries', 'unreadAlerts']">
      <template #fallback>
        <section aria-labelledby="attn-h" class="surface overflow-hidden" aria-busy="true">
          <h2 id="attn-h" class="px-5 pt-5 pb-2 text-base font-semibold">Needs your attention</h2>
          <div class="space-y-3 p-5">
            <Skeleton v-for="i in 2" :key="i" class="h-12 rounded-lg" />
          </div>
        </section>
      </template>
      <section aria-labelledby="attn-h" class="surface overflow-hidden">
        <h2 id="attn-h" class="px-5 pt-5 pb-2 text-base font-semibold">Needs your attention</h2>
        <ul v-if="attention.length" class="divide-y">
          <li v-for="a in attention" :key="a.key">
            <Link :href="a.href" prefetch class="hover:bg-muted/50 flex min-h-16 items-center gap-4 px-5 py-3 transition-colors">
              <span class="bg-muted text-muted-foreground flex size-10 shrink-0 items-center justify-center rounded-full"><component :is="a.icon" class="size-[18px]" aria-hidden="true" /></span>
              <span class="min-w-0 flex-1">
                <span class="block truncate text-sm font-medium">{{ a.title }}</span>
                <span class="text-muted-foreground block truncate text-sm">{{ a.text }}</span>
              </span>
              <span class="text-primary hidden text-sm font-medium sm:block">{{ a.cta }}</span>
              <ChevronRight class="text-muted-foreground size-4 shrink-0" aria-hidden="true" />
            </Link>
          </li>
        </ul>
        <EmptyState v-else :icon="PackageCheck" title="You are all caught up" description="No payments due, repairs open or deliveries on the way." class="py-10" />
      </section>
    </Deferred>

    <dl class="grid grid-cols-2 gap-3 lg:grid-cols-4">
      <Link v-for="s in summary" :key="s.label" :href="s.href" prefetch class="surface hover:bg-muted/50 block p-4 transition-colors">
        <dt class="text-muted-foreground text-sm">{{ s.label }}</dt>
        <dd class="price mt-1 text-2xl font-semibold">{{ s.value }}</dd>
      </Link>
    </dl>

    <section aria-labelledby="orders-h" class="surface overflow-hidden">
      <div class="flex items-center justify-between px-5 pt-5 pb-2">
        <h2 id="orders-h" class="text-base font-semibold">Recent orders</h2>
        <Link href="/account/orders" prefetch class="text-primary text-sm font-medium hover:underline">All orders</Link>
      </div>
      <ul v-if="recentOrders.length" class="divide-y">
        <li v-for="o in recentOrders" :key="o.id">
          <Link :href="`/account/orders/${o.number}`" prefetch class="hover:bg-muted/50 flex min-h-14 flex-wrap items-center gap-x-4 gap-y-1 px-5 py-3 transition-colors">
            <span class="price text-sm font-medium">{{ o.number }}</span>
            <StatusBadge :status="o.status" kind="order" />
            <span class="text-muted-foreground ml-auto text-sm">{{ fmtDate(o.createdAt, true) }}</span>
            <PriceTag :price="o.total" size="sm" class="w-24 justify-end" />
          </Link>
        </li>
      </ul>
      <EmptyState v-else title="No orders yet" description="Your orders and their delivery progress will show up here.">
        <Button as-child variant="outline"><Link href="/products" prefetch>Browse products</Link></Button>
      </EmptyState>
    </section>
  </div>
</template>
