<script setup lang="ts">
import { computed } from 'vue'
import { Head, Link, router } from '@inertiajs/vue3'
import { CreditCard, Lock } from '@lucide/vue'
import PageHeader from '@/components/PageHeader.vue'
import StatusBadge from '@/components/StatusBadge.vue'
import SegmentedControl from '@/components/SegmentedControl.vue'
import DataTable from '@/components/DataTable.vue'
import { money } from '@/lib/money'
import { fmtDate } from '@/lib/time'
import { statusLabel } from '@/lib/status'
import type { AdminPlanRow, NumberedPage } from '@/types'

const props = defineProps<{ plans: NumberedPage<AdminPlanRow>; statusFilter: string }>()
const rows = computed(() => props.plans?.data ?? [])

const statusOptions = [
  { value: 'any', label: 'Any status' },
  ...['active', 'completed', 'defaulted', 'cancelled'].map((s) => ({ value: s, label: statusLabel('plan', s) })),
]
const statusModel = computed({
  get: () => props.statusFilter || 'any',
  set: (value) => router.get('/admin/credit', { status: value === 'any' ? undefined : value }, { preserveScroll: true, preserveState: true }),
})

const exposure = rows.value
  .filter((p) => p.status === 'active')
  .reduce((sum, p) => sum + p.unpaid * p.amountPerInstallment, 0)
</script>

<template>
  <Head title="Credit (BNPL)" />

  <div class="flex flex-col gap-6">
    <PageHeader title="Installment plans" description="Every installment plan. Overdue payments pause a customer's new plans; 14 days overdue marks the plan defaulted." />

    <div class="surface flex flex-wrap items-center gap-3 p-4">
      <CreditCard class="size-4" />
      <span class="text-sm">Outstanding exposure (active plans):</span>
      <strong class="tabular-nums">{{ money(exposure) }}</strong>
    </div>

    <div class="surface overflow-hidden">
      <div class="border-b p-3">
        <SegmentedControl v-model="statusModel" label="Filter by status" class="scrollbar-none max-w-full overflow-x-auto" :options="statusOptions" />
      </div>
      <DataTable
        :columns="[
          { key: 'order', header: 'Order' },
          { key: 'customer', header: 'Customer', primary: true },
          { key: 'schedule', header: 'Schedule', align: 'right' },
          { key: 'arrears', header: 'Owing', align: 'right' },
          { key: 'status', header: 'Status', align: 'right' },
          { key: 'started', header: 'Started', align: 'right' },
        ]"
        :rows="rows"
        row-key="id"
        :row-href="(row) => `/admin/credit/${row.id}`"
        empty-title="No plans in this view"
      >
        <template #cell-order="{ row }">
          <Link :href="`/admin/orders/${row.orderNumber}`" prefetch cache-for="30s" class="tabular-nums inline-flex items-center text-sm font-semibold hover:underline pointer-coarse:min-h-11">{{ row.orderNumber }}</Link>
        </template>
        <template #cell-schedule="{ row }">
          <span class="text-sm">{{ row.installmentCount }}× {{ money(row.amountPerInstallment) }}</span>
          <span class="text-muted-foreground block text-xs">fee {{ money(row.feeTotal) }}</span>
        </template>
        <template #cell-customer="{ row }">
          <span class="block truncate">{{ row.customer }}</span>
          <span class="text-muted-foreground block truncate text-xs">{{ row.email }}</span>
        </template>
        <template #cell-arrears="{ row }">
          <span v-if="row.overdue" class="text-destructive font-medium">{{ row.overdue }} past due</span>
          <span v-if="row.unpaid" class="text-muted-foreground block text-xs">{{ row.unpaid }} outstanding</span>
          <span v-if="!row.overdue && !row.unpaid" class="text-muted-foreground text-xs">Nothing owing</span>
        </template>
        <template #cell-status="{ row }">
          <StatusBadge :status="row.status" kind="plan" />
        </template>
        <template #cell-started="{ row }">
          <span class="text-muted-foreground text-xs">{{ fmtDate(row.startedAt, true) }}</span>
        </template>
      </DataTable>
    </div>

    <p class="text-muted-foreground flex items-center gap-2 text-xs">
      <Lock class="size-3" />
      Finance guards live in checkout: max 2 active plans per customer, none while an installment is overdue.
    </p>
  </div>
</template>
