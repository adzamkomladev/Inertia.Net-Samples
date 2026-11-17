<script setup lang="ts">
import { Head, Link, router } from '@inertiajs/vue3'
import { computed, ref } from 'vue'
import { Search, ShoppingCart } from '@lucide/vue'
import PageHeader from '@/components/PageHeader.vue'
import StatusBadge from '@/components/StatusBadge.vue'
import SegmentedControl from '@/components/SegmentedControl.vue'
import DataTable from '@/components/DataTable.vue'
import PriceTag from '@/components/PriceTag.vue'
import { Input } from '@/components/ui/input'
import { fmtDate } from '@/lib/time'
import { paymentLook } from '@/lib/plan-labels'
import { statusLabel } from '@/lib/status'
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
const statusModel = computed({
  get: () => props.statusFilter || 'any',
  set: (value) =>
    router.get('/admin/orders', { status: value === 'any' ? undefined : value, q: searchTerm.value || undefined }, { preserveScroll: true, preserveState: true }),
})
const statusOptions = [{ value: 'any', label: 'Any status' }, ...statuses]
const payment = (row: AdminOrderRow) => paymentLook(row.status, row.paymentStatus, row.plan)
/** The delivery badge repeats the order status once the order is delivered. */
const showDelivery = (row: AdminOrderRow) => !!row.delivery && statusLabel('delivery', row.delivery.status) !== statusLabel('order', row.status)
const otherRecipient = (row: AdminOrderRow) => (row.recipientName && row.recipientName !== row.customerName ? row.recipientName : null)
const search = (e: Event) => {
  e.preventDefault()
  router.get('/admin/orders', { q: searchTerm.value || undefined, status: props.statusFilter || undefined }, { preserveState: true })
}
</script>

<template>
  <Head title="Orders" />

  <div class="flex flex-col gap-6">
    <PageHeader title="Orders" description="Confirm payments, move orders through fulfilment and issue refunds." />

    <div class="surface overflow-hidden">
      <div class="flex flex-col gap-3 border-b p-3 lg:flex-row lg:items-center">
        <SegmentedControl v-model="statusModel" label="Filter by status" class="scrollbar-none max-w-full overflow-x-auto" :options="statusOptions" />
        <form class="relative lg:ml-auto lg:w-72" @submit="search">
          <Search class="text-muted-foreground pointer-events-none absolute top-1/2 left-3 size-4 -translate-y-1/2" aria-hidden="true" />
          <Input v-model="searchTerm" type="search" class="pl-9" placeholder="Number, customer or email…" aria-label="Search orders" />
        </form>
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
          <span v-if="otherRecipient(row)" class="text-muted-foreground block truncate text-xs">Ship to {{ otherRecipient(row) }}</span>
        </template>
        <template #cell-payment="{ row }">
          <StatusBadge v-if="payment(row)" :status="payment(row)!.status" :kind="payment(row)!.kind" :label="payment(row)!.label || undefined" />
          <span v-else class="text-muted-foreground text-xs">Never paid</span>
          <span class="text-muted-foreground mt-0.5 block text-xs capitalize">{{ payLabel(row.paymentMethod) }}</span>
        </template>
        <template #cell-total="{ row }">
          <PriceTag :price="row.total" size="sm" />
        </template>
        <template #cell-delivery="{ row }">
          <StatusBadge v-if="showDelivery(row)" :status="row.delivery!.status" kind="delivery" />
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
              <StatusBadge v-if="payment(row) && (payment(row)!.kind === 'plan' || row.paymentStatus !== 'paid')" :status="payment(row)!.status" :kind="payment(row)!.kind" :label="payment(row)!.label || undefined" />
              <StatusBadge v-if="showDelivery(row)" :status="row.delivery!.status" kind="delivery" />
              <span class="text-muted-foreground ml-auto text-xs">{{ fmtDate(row.createdAt, true) }}</span>
            </span>
          </Link>
        </template>
      </DataTable>
    </div>
  </div>
</template>
