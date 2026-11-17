<script setup lang="ts">
import { Head, Link, router } from '@inertiajs/vue3'
import { computed, ref } from 'vue'
import { Search, ShoppingCart } from '@lucide/vue'
import PageHeader from '@/components/PageHeader.vue'
import StatusBadge from '@/components/StatusBadge.vue'
import DataTable from '@/components/DataTable.vue'
import PriceTag from '@/components/PriceTag.vue'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { fmtDate } from '@/lib/time'
import type { AdminOrderRow, NumberedPage } from '@/types'

const props = defineProps<{ orders: NumberedPage<AdminOrderRow>; statusFilter: string; q: string }>()
const rows = computed(() => props.orders?.data ?? [])
const searchTerm = ref(props.q)

const statuses = [
  { value: 'paid', label: 'Paid' },
  { value: 'processing', label: 'Processing' },
  { value: 'shipped', label: 'Shipped' },
  { value: 'delivered', label: 'Delivered' },
  { value: 'cancelled', label: 'Cancelled' },
]
const payLabel = (method: string) => method.replace(/_/g, ' ')
const setFilter = (value: string) =>
  router.get('/admin/orders', { status: value, q: searchTerm.value || undefined }, { preserveScroll: true, preserveState: true })
const search = (e: Event) => {
  e.preventDefault()
  router.get('/admin/orders', { q: searchTerm.value || undefined, status: props.statusFilter || undefined }, { preserveState: true })
}
</script>

<template>
  <Head title="Orders" />

  <div class="flex flex-col gap-6">
    <PageHeader title="Orders" description="Confirm payments, move orders through fulfilment and issue refunds.">
      <template #actions>
        <form class="flex w-full items-center gap-2 sm:w-auto" @submit="search">
          <div class="relative w-full sm:w-auto">
            <Search class="text-muted-foreground absolute top-1/2 left-3 size-4 -translate-y-1/2" />
            <Label for="order-search" class="sr-only">Search orders</Label>
            <Input id="order-search" v-model="searchTerm" type="search" class="w-full pl-9 sm:w-56" placeholder="Number / customer / email…" />
          </div>
        </form>
      </template>
    </PageHeader>

    <div class="surface overflow-hidden">
      <div class="flex flex-wrap gap-2 border-b p-3">
        <Button
          size="xs"
          :variant="props.statusFilter ? 'outline' : 'default'"
          class="rounded-full pointer-coarse:h-11"
          @click="setFilter('')"
        >
          Any status
        </Button>
        <Button
          v-for="s in statuses"
          :key="s.value"
          size="xs"
          :variant="props.statusFilter === s.value ? 'default' : 'outline'"
          class="rounded-full pointer-coarse:h-11"
          :aria-pressed="props.statusFilter === s.value"
          @click="setFilter(s.value)"
        >
          {{ s.label }}
        </Button>
      </div>
      <DataTable
        :columns="[
          { key: 'number', header: 'Order', primary: true },
          { key: 'customer', header: 'Customer' },
          { key: 'payment', header: 'Payment' },
          { key: 'total', header: 'Total', align: 'right' as const },
          { key: 'delivery', header: 'Delivery', align: 'right' as const },
          { key: 'status', header: 'Status', align: 'right' as const },
        ]"
        :rows="rows"
        row-key="id"
        :row-href="(row: any) => `/admin/orders/${row.number}`"
        :empty-icon="ShoppingCart"
        empty-title="No orders in this view"
      >
        <template #cell-number="{ row }">
          <span class="tabular-nums text-sm font-semibold">{{ row.number }}</span>
          <span class="text-muted-foreground block text-xs">{{ fmtDate(row.createdAt, true) }}</span>
        </template>
        <template #cell-customer="{ row }">
          <span class="block truncate text-sm">{{ row.customerName }}</span>
          <span class="text-muted-foreground block truncate text-xs">{{ row.customerEmail }}</span>
        </template>
        <template #cell-payment="{ row }">
          <StatusBadge :status="row.paymentStatus" kind="payment" />
          <span class="text-muted-foreground mt-0.5 block text-[11px] capitalize">{{ payLabel(row.paymentMethod) }}<template v-if="row.plan"> · {{ row.plan.count }}× plan ({{ row.plan.paid }} paid)</template></span>
        </template>
        <template #cell-total="{ row }">
          <PriceTag :price="row.total" size="sm" />
        </template>
        <template #cell-delivery="{ row }">
          <StatusBadge v-if="row.delivery" :status="row.delivery.status" kind="delivery" />
          <span v-else class="text-muted-foreground text-xs">—</span>
        </template>
        <template #cell-status="{ row }">
          <StatusBadge :status="row.status" kind="order" />
        </template>
        <template #card="{ row }">
          <Link :href="`/admin/orders/${row.number}`" prefetch cache-for="30s" class="block rounded-sm">
            <span class="flex min-h-11 items-center gap-2">
              <span class="tabular-nums text-sm font-semibold">{{ row.number }}</span>
              <span class="text-muted-foreground min-w-0 flex-1 truncate text-sm">{{ row.customerName }}</span>
              <PriceTag :price="row.total" size="sm" />
            </span>
            <span class="flex flex-wrap items-center gap-1.5 pb-1">
              <StatusBadge :status="row.status" kind="order" />
              <StatusBadge v-if="row.paymentStatus !== 'paid'" :status="row.paymentStatus" kind="payment" />
              <span v-if="row.plan" class="bg-accent text-accent-foreground rounded-full px-2 py-0.5 text-[11px] font-medium">{{ row.plan.count }}× plan · {{ row.plan.paid }} paid</span>
              <StatusBadge v-if="row.delivery" :status="row.delivery.status" kind="delivery" />
              <span class="text-muted-foreground ml-auto text-xs">{{ fmtDate(row.createdAt, true) }}</span>
            </span>
          </Link>
        </template>
      </DataTable>
    </div>
  </div>
</template>
