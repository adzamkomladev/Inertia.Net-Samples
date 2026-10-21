<script setup lang="ts">
import { computed } from 'vue'
import { CreditCard, Loader2 } from '@lucide/vue'
import StatusBadge from '@/components/StatusBadge.vue'
import { Button } from '@/components/ui/button'
import { money } from '@/lib/money'
import type { PlanView } from '@/types'

const props = withDefaults(
  defineProps<{
    plan: PlanView
    /** Order number shown in the header (account/credit has it via plan, orders page passes explicitly). */
    orderNumber?: string | null
    busyId?: string | null
    showPay?: boolean
  }>(),
  { orderNumber: null, busyId: null, showPay: true },
)

const emit = defineEmits<{ pay: [installmentId: string] }>()

const paidCount = computed(() => props.plan.installments.filter((i) => i.status === 'paid' || i.status === 'waived').length)
const progress = computed(() => (props.plan.installmentCount === 0 ? 0 : Math.round((paidCount.value / props.plan.installmentCount) * 100)))
const total = computed(() => props.plan.amountPerInstallment * props.plan.installmentCount)
</script>

<!-- BNPL plan card: progress, fee and the full installment schedule with per-row pay. -->
<template>
  <article class="bg-card overflow-hidden rounded-2xl border shadow-xs">
    <header class="flex flex-wrap items-center justify-between gap-3 border-b px-5 py-4">
      <div class="space-y-0.5">
        <p class="text-sm font-semibold">
          {{ plan.installmentCount }} installments
          <span v-if="orderNumber" class="text-muted-foreground price font-normal">· order {{ orderNumber }}</span>
        </p>
        <p class="text-muted-foreground text-xs">
          {{ money(plan.amountPerInstallment) }}/installment · {{ money(plan.feeTotal) }} plan fee · {{ money(total) }} total
        </p>
      </div>
      <StatusBadge :status="plan.status" kind="plan" />
    </header>

    <div class="border-b px-5 py-3">
      <div class="bg-muted flex items-center justify-between text-xs" role="progressbar" :aria-valuenow="progress" aria-valuemin="0" aria-valuemax="100">
        <span class="text-muted-foreground">{{ paidCount }} of {{ plan.installmentCount }} paid</span>
        <span class="price">{{ progress }}%</span>
      </div>
      <div class="bg-muted mt-1.5 h-1.5 overflow-hidden rounded-full">
        <div class="bg-success h-full rounded-full transition-all duration-500 ease-[var(--ease-out-soft)]" :style="{ width: `${progress}%` }" />
      </div>
    </div>

    <ul class="divide-y">
      <li v-for="row in plan.installments" :key="row.id" class="flex flex-wrap items-center justify-between gap-x-4 gap-y-2 px-5 py-3">
        <div class="flex items-center gap-3">
          <span class="bg-muted text-muted-foreground price flex size-7 items-center justify-center rounded-full text-xs">{{ row.number }}</span>
          <div class="text-sm">
            <p class="font-medium">{{ money(row.amount) }}</p>
            <p class="text-muted-foreground text-xs">
              due {{ new Date(row.dueDate + 'T00:00:00').toLocaleDateString(undefined, { month: 'short', day: 'numeric', year: 'numeric' }) }}
            </p>
          </div>
        </div>
        <div class="flex items-center gap-2">
          <StatusBadge :status="row.status" kind="installment" />
          <Button
            v-if="showPay && (row.status === 'due' || row.status === 'overdue')"
            size="xs"
            variant="outline"
            class="rounded-full"
            :disabled="busyId !== null"
            @click="emit('pay', row.id)"
          >
            <Loader2 v-if="busyId === row.id" class="animate-spin" aria-hidden="true" />
            <CreditCard v-else class="size-3" aria-hidden="true" />
            Pay now
          </Button>
        </div>
      </li>
    </ul>
  </article>
</template>
