<script setup lang="ts">
import { Head, router } from '@inertiajs/vue3'
import { computed, ref } from 'vue'
import { Search, ShoppingCart } from '@lucide/vue'
import PageHeader from '@/components/PageHeader.vue'
import StatusBadge from '@/components/StatusBadge.vue'
import DataTable from '@/components/DataTable.vue'
import PriceTag from '@/components/PriceTag.vue'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { fmtDate } from '@/lib/time'
import type { AdminOrderRow } from '@/types'

const props = defineProps<{ orders: { data: AdminOrderRow[]; page: number; hasMore: boolean }; statusFilter: string; q: string }>()
const rows = computed(() => props.orders?.data ?? [])
const searchTerm = ref(props.q)

const statuses = ['paid', 'processing', 'shipped', 'delivered', 'cancelled']
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
        <form class="flex items-center gap-2" @submit="search">
          <div class="relative">
            <Search class="text-muted-foreground absolute top-1/2 left-3 size-4 -translate-y-1/2" />
            <Input v-model="searchTerm" class="w-56 pl-9" placeholder="Number / customer / email…" />
          </div>
        </form>
      </template>
    </PageHeader>

    <div class="surface overflow-hidden">
      <div class="flex flex-wrap gap-2 border-b p-3">
        <Button
          size="xs"
          :variant="props.statusFilter ? 'outline' : 'default'"
          class="rounded-full"
          @click="setFilter('')"
        >
          Any status
        </Button>
        <Button
          v-for="s in statuses"
          :key="s"
          size="xs"
          :variant="props.statusFilter === s ? 'default' : 'outline'"
          class="rounded-full"
          @click="setFilter(s)"
        >
          {{ s }}
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
          <span class="text-muted-foreground mt-0.5 block text-[11px]">{{ row.paymentMethod.replace('_', ' ') }}</span>
        </template>
        <template #cell-total="{ row }">
          <PriceTag :price="row.total" size="sm" />
        </template>
        <template #cell-delivery="{ row }">
          <StatusBadge v-if="row.delivery" :status="row.delivery.status" kind="delivery" />
          <span v-else class="text-muted-foreground text-xs">—</span>
          <span v-if="row.unassigned" class="text-primary ml-1 block text-[11px] font-medium">assign a rider</span>
        </template>
        <template #cell-status="{ row }">
          <StatusBadge :status="row.status" kind="order" />
        </template>
      </DataTable>
    </div>
  </div>
</template>
