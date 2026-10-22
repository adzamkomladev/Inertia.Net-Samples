<script setup lang="ts">
import { Head, Link, router } from '@inertiajs/vue3'
import { computed } from 'vue'
import { BadgeCheck, ShieldCheck, Wrench } from '@lucide/vue'
import PageHeader from '@/components/PageHeader.vue'
import StatusBadge from '@/components/StatusBadge.vue'
import EmptyState from '@/components/EmptyState.vue'
import { Button } from '@/components/ui/button'
import { money } from '@/lib/money'
import { fmtDate, fmtDateTime } from '@/lib/time'
import type { InsuranceClaimRow, RepairRow, WarrantyClaimRow } from '@/types'

const props = defineProps<{
  tab: 'warranties' | 'insurance' | 'repairs'
  warrantyClaims: WarrantyClaimRow[]
  insuranceClaims: InsuranceClaimRow[]
  repairs: RepairRow[]
}>()

const tabs = [
  {
    id: 'warranties',
    label: 'Warranty claims',
    count: computed(
      () =>
        props.warrantyClaims.filter(
          (c) => c.status === 'submitted' || c.status === 'in_review',
        ).length,
    ),
  },
  {
    id: 'insurance',
    label: 'Insurance claims',
    count: computed(
      () =>
        props.insuranceClaims.filter(
          (c) =>
            c.status === 'submitted' ||
            c.status === 'under_review' ||
            c.status === 'approved',
        ).length,
    ),
  },
  {
    id: 'repairs',
    label: 'Repairs',
    count: computed(
      () =>
        props.repairs.filter(
          (r) => r.status === 'received' || r.status === 'quoted',
        ).length,
    ),
  },
]

const setTab = (id: string) =>
  router.get('/admin/care', id === 'warranties' ? {} : { tab: id }, {
    preserveScroll: true,
    preserveState: true,
  })
</script>

<template>
  <Head title="Service desk" />

  <div class="flex flex-col gap-6">
    <PageHeader
      title="Care desk"
      description="Warranty claims, insurance claims and repairs. Each decision notifies the customer."
    />

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
        <span
          v-if="props.tab === t.id"
          :class="
            t.count.value
              ? 'bg-warning/15 text-warning'
              : 'bg-muted text-muted-foreground'
          "
          class="ml-1 rounded-full px-1.5 text-xs tabular-nums"
        >
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
          <span class="text-muted-foreground ml-auto text-xs"
            >{{ c.orderNumber }} · {{ c.customer }} ·
            {{ fmtDate(c.createdAt, true) }}</span
          >
        </div>
        <Link
          :href="`/admin/care/warranty-claims/${c.id}`"
          class="mt-3 inline-block text-sm text-primary underline"
          >Open details &rarr;</Link
        >
        <p class="mt-1 tabular-nums text-xs">{{ c.serial ?? 'no serial' }}</p>
        <p class="mt-2 text-sm leading-6">{{ c.issue }}</p>
        <p v-if="c.decisionNote" class="text-muted-foreground mt-1 text-xs">
          Decision: {{ c.decisionNote }}
        </p>
      </div>
      <EmptyState
        v-if="!props.warrantyClaims.length"
        :icon="ShieldCheck"
        title="No warranty claims"
        description="Claims arrive here inside the coverage window, newest first."
      />
    </section>

    <!-- insurance claims -->
    <section v-else-if="tab === 'insurance'" class="flex flex-col gap-3">
      <div v-for="c in insuranceClaims" :key="c.id" class="surface p-4">
        <div class="flex flex-wrap items-center gap-x-3 gap-y-1">
          <BadgeCheck class="size-4" />
          <span class="text-sm font-semibold capitalize">{{
            c.incidentType
          }}</span>
          <StatusBadge :status="c.status" kind="insurance" />
          <span class="text-muted-foreground ml-auto text-xs"
            >{{ c.device }} · {{ c.customer }} · incident
            {{ fmtDate(c.incidentOn, false) }}</span
          >
        </div>
        <Link
          :href="`/admin/care/insurance-claims/${c.id}`"
          class="mt-3 inline-block text-sm text-primary underline"
          >Open details &rarr;</Link
        >
        <p class="mt-2 text-sm leading-6">{{ c.description }}</p>
        <p class="mt-1 tabular-nums text-xs">
          claim {{ money(c.payoutAmount ?? c.coverage) }} — approved payout
          lands as mobile money
        </p>

        <div v-if="c.paidAt" class="text-muted-foreground text-xs">
          paid {{ fmtDateTime(c.paidAt) }}
        </div>
      </div>
      <EmptyState
        v-if="!props.insuranceClaims.length"
        :icon="BadgeCheck"
        title="No insurance claims"
        description="Spot-safe claims appear here when customers file them."
      />
    </section>

    <!-- repairs -->
    <section v-else class="flex flex-col gap-3">
      <div v-for="r in repairs" :key="r.id" class="surface p-4">
        <div class="flex flex-wrap items-center gap-x-3 gap-y-1">
          <Wrench class="size-4" />
          <span class="text-sm font-semibold">{{ r.deviceName }}</span>
          <StatusBadge :status="r.status" kind="repair" />
          <span class="text-muted-foreground ml-auto text-xs"
            >{{ r.customer ?? '' }} · opened
            {{ fmtDate(r.createdAt, true) }}</span
          >
        </div>
        <Link
          :href="`/admin/care/repairs/${r.id}`"
          class="mt-3 inline-block text-sm text-primary underline"
          >Open details &rarr;</Link
        >
        <p class="mt-2 text-sm leading-6">{{ r.problem }}</p>
        <p class="text-muted-foreground mt-1 tabular-nums text-xs">
          {{ r.deviceSerial ?? 'no serial'
          }}<span v-if="r.quotedCost != null">
            · quoted {{ money(r.quotedCost) }}</span
          >
        </p>
      </div>
      <EmptyState
        v-if="!props.repairs.length"
        :icon="Wrench"
        title="No repair tickets"
        description="Customer-booked devices show up here with their story and status history."
      />
    </section>
  </div>
</template>
