<script setup lang="ts">
import { computed } from 'vue'
import { Head, router } from '@inertiajs/vue3'
import { CreditCard, Lock } from '@lucide/vue'
import PageHeader from '@/components/PageHeader.vue'
import StatusBadge from '@/components/StatusBadge.vue'
import DataTable from '@/components/DataTable.vue'
import { Button } from '@/components/ui/button'
import { money } from '@/lib/money'
import { fmtDate } from '@/lib/time'
import type { AdminPlanRow } from '@/types'

const props = defineProps<{ plans: { data: AdminPlanRow[]; page: number; hasMore: boolean }; statusFilter: string }>()
const rows = computed(() => props.plans?.data ?? [])

const statuses = ['active', 'completed', 'defaulted', 'cancelled']
const setFilter = (value: string) =>
  router.get('/admin/credit', { status: value || undefined }, { preserveScroll: true, preserveState: true })

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
      <strong class="tabular-nums tabular-nums">{{ money(exposure) }}</strong>
      <div class="ml-auto flex flex-wrap gap-1.5">
        <Button size="xs" :variant="!statusFilter ? 'default' : 'outline'" class="rounded-full" @click="setFilter('')">Any</Button>
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
    </div>

    <div class="surface overflow-hidden">
      <DataTable
        :columns="[
          { key: 'order', header: 'Order' },
          { key: 'customer', header: 'Customer', primary: true },
          { key: 'schedule', header: 'Schedule', align: 'right' },
          { key: 'arrears', header: 'Arrears', align: 'right' },
          { key: 'status', header: 'Status', align: 'right' },
          { key: 'started', header: 'Started', align: 'right' },
        ]"
        :rows="rows"
        row-key="id"
        empty-title="No plans in this view"
      >
        <template #cell-order="{ row }">
          <a :href="`/admin/orders/${row.orderNumber}`" class="tabular-nums text-sm font-semibold hover:underline">{{ row.orderNumber }}</a>
        </template>
        <template #cell-schedule="{ row }">
          <span class="text-sm">{{ row.installmentCount }}× {{ money(row.amountPerInstallment) }}</span>
          <span class="text-muted-foreground block text-[11px]">fee {{ money(row.feeTotal) }}</span>
        </template>
        <template #cell-customer="{ row }">
          <span class="block truncate">{{ row.customer }}</span>
          <span class="text-muted-foreground block truncate text-xs">{{ row.email }}</span>
        </template>
        <template #cell-arrears="{ row }">
          <span v-if="row.overdue" class="text-destructive font-medium">{{ row.overdue }} overdue</span>
          <span v-if="row.unpaid" class="text-muted-foreground block text-[11px]">{{ row.unpaid }} outstanding</span>
          <span v-if="!row.overdue && !row.unpaid" class="text-muted-foreground text-xs">nothing</span>
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
