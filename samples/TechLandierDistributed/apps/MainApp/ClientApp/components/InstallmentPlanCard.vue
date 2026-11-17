<script setup lang="ts">
import { computed, ref } from 'vue'
import { CreditCard, FileDown, Loader2 } from '@lucide/vue'
import ConfirmDialog from '@/components/ConfirmDialog.vue'
import StatusBadge from '@/components/StatusBadge.vue'
import { Button } from '@/components/ui/button'
import { nextInstallment, rowStatus } from '@/lib/installments'
import { money } from '@/lib/money'
import { fmtDate } from '@/lib/time'
import type { PlanView } from '@/types'

const props = withDefaults(
  defineProps<{
    plan: PlanView
    /** Order number shown in the header (account/credit has it via plan, orders page passes explicitly). */
    orderNumber?: string | null
    busyId?: string | null
    /** Owner view: shows the Pay button and the statement download. Off for staff and read-only guests. */
    showPay?: boolean
  }>(),
  { orderNumber: null, busyId: null, showPay: true },
)

const emit = defineEmits<{ pay: [installmentId: string] }>()

const paidCount = computed(() => props.plan.installments.filter((i) => i.status === 'paid').length)
const waivedCount = computed(() => props.plan.installments.filter((i) => i.status === 'waived').length)
const progress = computed(() => (props.plan.installmentCount === 0 ? 0 : Math.round((paidCount.value / props.plan.installmentCount) * 100)))
const shownOrder = computed(() => props.orderNumber ?? props.plan.orderNumber)
const total = computed(() => props.plan.amountPerInstallment * props.plan.installmentCount)
const next = computed(() => nextInstallment(props.plan.installments))
const payable = computed(() => props.showPay && props.plan.status === 'active' ? next.value : null)

const confirming = ref(false)
const confirm = () => {
  confirming.value = false
  if (payable.value) emit('pay', payable.value.id)
}
</script>

<!-- BNPL plan card: progress, fee, one Pay button for the next installment and the full schedule. -->
<template>
  <article class="surface overflow-hidden">
    <header class="flex flex-wrap items-center justify-between gap-3 border-b px-5 py-4">
      <div class="space-y-0.5">
        <p class="text-sm font-semibold">
          {{ plan.installmentCount }} installments of {{ money(plan.amountPerInstallment) }}
          <span v-if="shownOrder" class="text-muted-foreground price font-normal">· order {{ shownOrder }}</span>
        </p>
        <p v-if="plan.productName" class="text-muted-foreground text-xs">{{ plan.productName }}</p>
        <p class="text-muted-foreground text-xs">
          {{ money(total) }} in total, including a {{ money(plan.feeTotal) }} plan fee
        </p>
      </div>
      <div class="flex items-center gap-2">
        <!-- Owner-only PDF: staff views (showPay off) do not get it. -->
        <Button v-if="showPay" as-child size="xs" variant="outline" class="rounded-full">
          <a :href="`/account/installments/${plan.id}/statement.pdf`" download><FileDown aria-hidden="true" />Statement (PDF)</a>
        </Button>
        <StatusBadge :status="plan.status" kind="plan" />
      </div>
    </header>

    <div class="border-b px-5 py-3">
      <div class="flex items-center justify-between text-xs" role="progressbar" :aria-valuenow="progress" aria-valuemin="0" aria-valuemax="100" aria-label="Plan progress">
        <span class="text-muted-foreground">{{ paidCount }} of {{ plan.installmentCount }} paid<template v-if="waivedCount">, {{ waivedCount }} waived</template></span>
        <span class="price">{{ progress }}%</span>
      </div>
      <div class="bg-muted mt-1.5 h-2 overflow-hidden rounded-full">
        <div class="bg-primary h-full rounded-full transition-all duration-500" :style="{ width: `${progress}%` }" />
      </div>
    </div>

    <div v-if="payable" class="bg-muted/40 flex flex-wrap items-center justify-between gap-3 border-b px-5 py-3.5">
      <p class="text-sm">
        <span class="text-muted-foreground block text-xs">{{ payable.status === 'overdue' ? 'Overdue payment' : 'Next payment' }}</span>
        <span class="price font-semibold">{{ money(payable.amount) }}</span>
        <span class="text-muted-foreground"> · due {{ fmtDate(payable.dueDate, true) }}</span>
      </p>
      <Button size="sm" :disabled="busyId !== null" @click="confirming = true">
        <Loader2 v-if="busyId === payable.id" class="animate-spin" aria-hidden="true" />
        <CreditCard v-else aria-hidden="true" />
        Pay {{ money(payable.amount) }}
      </Button>
    </div>

    <ul class="divide-y">
      <li v-for="row in plan.installments" :key="row.id" class="flex flex-wrap items-center justify-between gap-x-4 gap-y-2 px-5 py-3">
        <div class="flex items-center gap-3">
          <span class="bg-muted text-muted-foreground price flex size-8 items-center justify-center rounded-full text-xs">{{ row.number }}</span>
          <div class="text-sm">
            <p class="font-medium">{{ money(row.amount) }}</p>
            <p class="text-muted-foreground text-xs">
              due {{ fmtDate(row.dueDate, true) }}
              <span v-if="row.paidAt" class="block">Paid {{ fmtDate(row.paidAt, true) }}</span>
            </p>
          </div>
        </div>
        <StatusBadge :status="rowStatus(row, next)" kind="installment" />
      </li>
    </ul>

    <ConfirmDialog
      v-if="payable"
      v-model:open="confirming"
      :title="`Pay ${money(payable.amount)} now?`"
      :description="`This settles installment ${payable.number} of ${plan.installmentCount}${shownOrder ? ` on order ${shownOrder}` : ''}, due ${fmtDate(payable.dueDate, true)}. It is charged immediately and cannot be undone.`"
      :confirm-label="`Pay ${money(payable.amount)}`"
      @confirm="confirm"
    />
  </article>
</template>
