<script setup lang="ts">
import { Head, router, useForm } from '@inertiajs/vue3'
import { ref } from 'vue'
import { computed } from 'vue'
import { BadgeCheck, ShieldCheck, Wrench } from '@lucide/vue'
import PageHeader from '@/components/PageHeader.vue'
import StatusBadge from '@/components/StatusBadge.vue'
import EmptyState from '@/components/EmptyState.vue'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select'
import { Textarea } from '@/components/ui/textarea'
import { money } from '@/lib/money'
import { fmtDate, fmtDateTime } from '@/lib/time'
import type { InsuranceClaimRow, RepairRow, WarrantyClaimRow } from '@/types'

const props = defineProps<{
  tab: 'warranties' | 'insurance' | 'repairs'
  warrantyClaims: WarrantyClaimRow[]
  insuranceClaims: InsuranceClaimRow[]
  repairs: RepairRow[]
}>()

const warrantyForm = useForm({ decision: 'approve', note: '' })
const insuranceForm = useForm({ decision: 'approve', note: '', payout: 0 })
const repairForm = useForm({ status: 'quoted', quotedCost: 0, technicianName: '', note: '' })
const busy = ref<string | null>(null)

const tabs = [
  { id: 'warranties', label: 'Warranty claims', count: computed(() => props.warrantyClaims.filter((c) => c.status === 'submitted' || c.status === 'in_review').length) },
  { id: 'insurance', label: 'Insurance claims', count: computed(() => props.insuranceClaims.filter((c) => c.status === 'submitted' || c.status === 'under_review').length) },
  { id: 'repairs', label: 'Repairs', count: computed(() => props.repairs.filter((r) => r.status === 'received' || r.status === 'quoted').length) },
]

const setTab = (id: string) => router.get('/admin/care', id === 'warranties' ? {} : { tab: id }, { preserveScroll: true, preserveState: true })

const decideWarranty = (claim: WarrantyClaimRow) => {
  busy.value = claim.id
  warrantyForm.post(`/admin/care/warranty-claims/${claim.id}`, { onFinish: () => (busy.value = null) })
}
const decideInsurance = (claim: InsuranceClaimRow) => {
  busy.value = claim.id
  insuranceForm.post(`/admin/care/insurance-claims/${claim.id}`, { onFinish: () => (busy.value = null) })
}
const updateRepair = (repair: RepairRow) => {
  busy.value = repair.id
  repairForm.post(`/admin/care/repairs/${repair.id}`, { onFinish: () => (busy.value = null) })
}
</script>

<template>
  <Head title="Service desk" />

  <div class="flex flex-col gap-6">
    <PageHeader title="Care desk" description="Warranty claims, insurance claims and repairs. Each decision notifies the customer." />

    <div class="flex flex-wrap gap-2">
      <Button
        v-for="t in tabs"
        :key="t.id"
        :variant="props.tab === t.id ? 'default' : 'outline'"
        class="rounded-full"
        @click="setTab(t.id)"
      >
        {{ t.label }}
        <!-- Only the open tab's list is loaded, so the other tabs have no count to show. -->
        <span v-if="props.tab === t.id" :class="t.count.value ? 'bg-warning/15 text-warning' : 'bg-muted text-muted-foreground'" class="ml-1 rounded-full px-1.5 text-xs tabular-nums">
          {{ t.count.value }}
        </span>
      </Button>
    </div>

    <!-- warranty claims -->
    <section v-if="tab === 'warranties'" class="flex flex-col gap-3">
      <div v-for="c in warrantyClaims" :key="c.id" class="surface p-4">
        <div class="flex flex-wrap items-center gap-x-3 gap-y-1">
          <ShieldCheck class="size-4" />
          <span class="text-sm font-semibold">{{ c.item }}</span>
          <StatusBadge :status="c.status" kind="warranty" />
          <span class="text-muted-foreground ml-auto text-xs">{{ c.orderNumber }} · {{ c.customer }} · {{ fmtDate(c.createdAt, true) }}</span>
        </div>
        <p class="mt-1 tabular-nums text-xs">{{ c.serial ?? 'no serial' }}</p>
        <p class="mt-2 text-sm leading-6">{{ c.issue }}</p>
        <p v-if="c.decisionNote" class="text-muted-foreground mt-1 text-xs">Decision: {{ c.decisionNote }}</p>
        <div v-if="c.status === 'submitted' || c.status === 'in_review'" class="mt-3 flex flex-wrap items-center gap-2">
          <Input v-model="warrantyForm.decision" class="w-32 hidden" />
          <Button size="xs" class="rounded-full" :disabled="busy === c.id" @click="decideWarranty({ ...c } as WarrantyClaimRow)">Approve & ship</Button>
          <Button size="xs" variant="destructive" class="rounded-full" :disabled="busy === c.id" @click="warrantyForm.decision = 'reject'; decideWarranty(c)">Reject</Button>
          <Textarea v-model="warrantyForm.note" class="w-full" rows="2" maxlength="200" placeholder="Decision note (required for rejects)" />
        </div>
      </div>
      <EmptyState v-if="!props.warrantyClaims.length" :icon="ShieldCheck" title="No warranty claims" description="Claims arrive here inside the coverage window, newest first." />
    </section>

    <!-- insurance claims -->
    <section v-else-if="tab === 'insurance'" class="flex flex-col gap-3">
      <div v-for="c in insuranceClaims" :key="c.id" class="surface p-4">
        <div class="flex flex-wrap items-center gap-x-3 gap-y-1">
          <BadgeCheck class="size-4" />
          <span class="text-sm font-semibold capitalize">{{ c.incidentType }}</span>
          <StatusBadge :status="c.status" kind="insurance" />
          <span class="text-muted-foreground ml-auto text-xs">{{ c.device }} · {{ c.customer }} · incident {{ fmtDate(c.incidentOn, false) }}</span>
        </div>
        <p class="mt-2 text-sm leading-6">{{ c.description }}</p>
        <p class="mt-1 tabular-nums text-xs">claim {{ money(c.payoutAmount ?? c.coverage) }} — approved payout lands as mobile money</p>
        <div v-if="c.status === 'submitted' || c.status === 'under_review'" class="mt-3 flex flex-wrap items-center gap-2">
          <Button size="xs" class="rounded-full" :disabled="busy === c.id" @click="insuranceForm.decision = 'approve'; decideInsurance(c)">Approve</Button>
          <Button size="xs" class="rounded-full" :disabled="busy === c.id" @click="insuranceForm.decision = 'paid'; decideInsurance(c)">Pay out</Button>
          <Button size="xs" variant="destructive" class="rounded-full" :disabled="busy === c.id" @click="insuranceForm.decision = 'reject'; decideInsurance(c)">Reject</Button>
          <Textarea v-model="insuranceForm.note" class="w-full" rows="2" maxlength="200" placeholder="Decision note" />
        </div>
        <div v-else-if="c.paidAt" class="text-muted-foreground text-xs">paid {{ fmtDateTime(c.paidAt) }}</div>
      </div>
      <EmptyState v-if="!props.insuranceClaims.length" :icon="BadgeCheck" title="No insurance claims" description="Spot-safe claims appear here when customers file them." />
    </section>

    <!-- repairs -->
    <section v-else class="flex flex-col gap-3">
      <div v-for="r in repairs" :key="r.id" class="surface p-4">
        <div class="flex flex-wrap items-center gap-x-3 gap-y-1">
          <Wrench class="size-4" />
          <span class="text-sm font-semibold">{{ r.deviceName }}</span>
          <StatusBadge :status="r.status" kind="repair" />
          <span class="text-muted-foreground ml-auto text-xs">{{ r.customer ?? '' }} · opened {{ fmtDate(r.createdAt, true) }}</span>
        </div>
        <p class="mt-2 text-sm leading-6">{{ r.problem }}</p>
        <p class="text-muted-foreground mt-1 tabular-nums text-xs">{{ r.deviceSerial ?? 'no serial' }}<span v-if="r.quotedCost != null"> · quoted {{ money(r.quotedCost) }}</span></p>
        <div v-if="r.status !== 'closed' && r.status !== 'picked_up'" class="mt-3 flex flex-wrap items-center gap-2">
          <Select v-model="repairForm.status">
            <SelectTrigger class="w-44"><SelectValue placeholder="Next status" /></SelectTrigger>
            <SelectContent>
              <SelectItem value="quoted">quoted (+ quoted cost)</SelectItem>
              <SelectItem value="repairing">repairing</SelectItem>
              <SelectItem value="ready">ready for pickup</SelectItem>
              <SelectItem value="picked_up">picked up</SelectItem>
              <SelectItem value="closed">close</SelectItem>
            </SelectContent>
          </Select>
          <Input v-if="repairForm.status === 'quoted' && r.quotedCost == null" v-model="repairForm.quotedCost" type="number" min="0" step="5" class="w-28" placeholder="$ quote" />
          <Input v-model="repairForm.technicianName" class="w-36" maxlength="60" placeholder="Technician" />
          <Input v-model="repairForm.note" class="w-64" maxlength="160" placeholder="Note (shown to the customer)" />
          <Button size="xs" :disabled="busy === r.id" @click="updateRepair(r)">Update</Button>
        </div>
      </div>
      <EmptyState v-if="!props.repairs.length" :icon="Wrench" title="No repair tickets" description="Customer-booked devices show up here with their story and status history." />
    </section>
  </div>
</template>
