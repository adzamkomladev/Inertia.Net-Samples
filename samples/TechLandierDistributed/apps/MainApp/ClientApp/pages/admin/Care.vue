<script setup lang="ts">
import { Head, router } from '@inertiajs/vue3'
import { computed, ref } from 'vue'
import { BadgeCheck, Search, ShieldCheck, Wrench } from '@lucide/vue'
import DataTable from '@/components/DataTable.vue'
import PageHeader from '@/components/PageHeader.vue'
import StatusBadge from '@/components/StatusBadge.vue'
import EmptyState from '@/components/EmptyState.vue'
import SegmentedControl from '@/components/SegmentedControl.vue'
import RelativeTime from '@/components/RelativeTime.vue'
import RepairProgress from '@/components/care/RepairProgress.vue'
import { Input } from '@/components/ui/input'
import { money } from '@/lib/money'
import { fmtDate } from '@/lib/time'
import { Button } from '@/components/ui/button'
import type { InsuranceClaimRow, NumberedPage, RepairRow, WarrantyClaimRow } from '@/types'

const props = defineProps<{
  tab: 'warranties' | 'insurance' | 'repairs'
  show: 'open' | 'all'
  warrantyClaims: NumberedPage<WarrantyClaimRow>
  insuranceClaims: NumberedPage<InsuranceClaimRow>
  repairs: NumberedPage<RepairRow>
}>()

type Row = { id: string; href: string; icon: typeof Wrench; title: string; status: string; kind: string; sub: string; body: string; amount: number | null; at: string; repair?: boolean }

const rows = computed<Row[]>(() => {
  if (props.tab === 'insurance')
    return props.insuranceClaims.data.map((c) => ({
      id: c.id, href: `/admin/care/insurance-claims/${c.id}`, icon: BadgeCheck, title: c.device, status: c.status, kind: 'insurance',
      sub: `${c.customer} · ${c.incidentType} on ${fmtDate(c.incidentOn, true)}`, body: c.description, amount: c.payoutAmount ?? c.coverage, at: c.createdAt,
    }))
  if (props.tab === 'repairs')
    return props.repairs.data.map((r) => ({
      id: r.id, href: `/admin/care/repairs/${r.id}`, icon: Wrench, title: r.deviceName, status: r.status, kind: 'repair',
      sub: `${r.customer ?? 'Customer'}${r.deviceSerial ? ` · ${r.deviceSerial}` : ''}`, body: r.problem, amount: r.quotedCost, at: r.createdAt, repair: true,
    }))
  return props.warrantyClaims.data.map((c) => ({
    id: c.id, href: `/admin/care/warranty-claims/${c.id}`, icon: ShieldCheck, title: c.item, status: c.status, kind: 'warranty',
    sub: `${c.customer} · ${c.orderNumber}${c.serial ? ` · ${c.serial}` : ''}`, body: c.decisionNote ? `${c.issue} — ${c.decisionNote}` : c.issue, amount: null, at: c.createdAt,
  }))
})

// The server filters by show and pages the queue; the search box narrows the rows on the current page.
const query = ref('')
const current = computed(() => (props.tab === 'insurance' ? props.insuranceClaims : props.tab === 'repairs' ? props.repairs : props.warrantyClaims))
const visible = computed(() => {
  const q = query.value.trim().toLowerCase()
  return rows.value.filter((r) => !q || [r.title, r.sub, r.body].some((v) => v.toLowerCase().includes(q)))
})

const loading = ref(false)
function go(params: { tab?: string; show?: string; page?: number }) {
  const tab = params.tab ?? props.tab
  const show = params.show ?? props.show
  router.get('/admin/care', {
    tab: tab === 'warranties' ? undefined : tab, show: show === 'open' ? undefined : show, page: params.page && params.page > 1 ? params.page : undefined,
  }, { preserveScroll: true, preserveState: true, onStart: () => (loading.value = true), onFinish: () => (loading.value = false) })
}
const tabModel = computed({ get: () => props.tab, set: (id: string) => go({ tab: id, page: 1 }) })
const showModel = computed({ get: () => props.show, set: (value: string) => go({ show: value, page: 1 }) })
const empty = { warranties: 'No warranty claims', insurance: 'No insurance claims', repairs: 'No repair tickets' }
</script>

<template>
  <Head title="Care desk" />

  <div class="flex flex-col gap-6">
    <PageHeader title="Care desk" description="Warranty claims, insurance claims and repairs. Each decision notifies the customer." />

    <SegmentedControl
      v-model="tabModel" label="Care queue" class="scrollbar-none max-w-full self-start overflow-x-auto"
      :options="[{ value: 'warranties', label: 'Warranty claims' }, { value: 'insurance', label: 'Insurance claims' }, { value: 'repairs', label: 'Repairs' }]"
    />

    <section class="surface overflow-hidden">
      <div class="flex flex-col gap-3 border-b p-3 sm:flex-row sm:items-center">
        <SegmentedControl v-model="showModel" label="Show" class="scrollbar-none max-w-full overflow-x-auto" :options="[{ value: 'open', label: 'Needs action' }, { value: 'all', label: 'All' }]" />
        <div class="relative sm:ml-auto sm:w-64">
          <Search class="text-muted-foreground pointer-events-none absolute top-1/2 left-3 size-4 -translate-y-1/2" aria-hidden="true" />
          <Input v-model="query" type="search" placeholder="Device, customer, issue…" class="pl-9" aria-label="Search the queue" />
        </div>
      </div>

      <DataTable
        v-if="visible.length" bare row-key="id" :rows="visible" :row-href="(r: any) => r.href" :class="loading && 'opacity-60'" class="transition-opacity"
        :columns="[
          { key: 'title', label: 'Case', primary: true },
          { key: 'status', label: 'Status' },
          { key: 'progress', label: 'Progress', hideLabel: true },
          { key: 'amount', label: 'Amount', align: 'right' as const },
          { key: 'at', label: 'Filed', align: 'right' as const },
        ]"
      >
        <template #cell-title="{ row: r }">
          <span class="flex min-w-0 items-start gap-3">
            <span class="bg-muted text-muted-foreground flex size-10 shrink-0 items-center justify-center rounded-full"><component :is="r.icon" class="size-4" aria-hidden="true" /></span>
            <span class="min-w-0">
              <span class="block truncate font-medium">{{ r.title }}</span>
              <span class="text-muted-foreground block truncate text-xs font-normal">{{ r.sub }}</span>
              <span class="text-muted-foreground line-clamp-1 text-sm font-normal">{{ r.body }}</span>
            </span>
          </span>
        </template>
        <template #cell-status="{ row: r }"><StatusBadge :status="r.status" :kind="r.kind" /></template>
        <template #cell-progress="{ row: r }"><RepairProgress v-if="r.repair" :status="r.status" compact /></template>
        <template #cell-amount="{ row: r }"><span v-if="r.amount != null" class="price text-sm font-medium">{{ money(r.amount) }}</span><span v-else class="text-muted-foreground text-xs">—</span></template>
        <template #cell-at="{ row: r }"><span class="text-muted-foreground text-xs"><RelativeTime :iso="r.at" /></span></template>
      </DataTable>
      <EmptyState
        v-else :icon="query ? Search : tab === 'repairs' ? Wrench : tab === 'insurance' ? BadgeCheck : ShieldCheck"
        :title="query ? 'Nothing matches' : show === 'open' ? 'All caught up' : empty[tab]"
        :description="query ? 'Try a device, customer or order number.' : 'New cases land here as customers file them, newest first.'"
      />

      <nav v-if="current.currentPage > 1 || current.hasMore" class="flex items-center justify-between gap-3 border-t p-3" aria-label="Care queue pages">
        <Button variant="ghost" size="sm" class="pointer-coarse:h-11" :disabled="current.currentPage <= 1 || loading" @click="go({ page: current.currentPage - 1 })">Newer</Button>
        <span class="text-muted-foreground text-xs tabular-nums">Page {{ current.currentPage }}<template v-if="current.lastPage"> of {{ current.lastPage }}</template></span>
        <Button variant="ghost" size="sm" class="pointer-coarse:h-11" :disabled="!current.hasMore || loading" @click="go({ page: current.currentPage + 1 })">Older</Button>
      </nav>
    </section>
  </div>
</template>
