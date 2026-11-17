<script setup lang="ts">
import { computed, ref } from 'vue'
import { Head, Link, useForm } from '@inertiajs/vue3'
import { ChevronRight, CreditCard, Lock } from '@lucide/vue'
import EmptyState from '@/components/EmptyState.vue'
import InstallmentPlanCard from '@/components/InstallmentPlanCard.vue'
import PageHeader from '@/components/PageHeader.vue'
import StatCard from '@/components/StatCard.vue'
import { Button } from '@/components/ui/button'
import { money } from '@/lib/money'
import { nextInstallment } from '@/lib/installments'
import { fmtDate } from '@/lib/time'
import type { PlanView } from '@/types'

const props = defineProps<{ plans: PlanView[]; blocked: boolean }>()

const busy = ref<string | null>(null)
const payForm = useForm({})
const pay = (id: string) => {
  busy.value = id
  payForm.post(`/account/credit/installments/${id}/pay`, { onFinish: () => (busy.value = null) })
}

const activePlans = computed(() => props.plans.filter((p) => p.status === 'active'))
// Active (and defaulted, which still needs attention) plans lead; completed and cancelled ones collapse.
const isPast = (p: PlanView) => p.status === 'completed' || p.status === 'cancelled'
const currentPlans = computed(() => props.plans.filter((p) => !isPast(p)))
const pastPlans = computed(() => props.plans.filter(isPast))
const open = computed(() => props.plans.flatMap((p) => p.installments.filter((i) => i.status === 'due' || i.status === 'overdue')))
const overdue = computed(() => open.value.filter((i) => i.status === 'overdue'))
const outstanding = computed(() => open.value.reduce((sum, i) => sum + i.amount, 0))
// The soonest unpaid installment across active plans, so the one payment that matters is named first.
const next = computed(() =>
  activePlans.value.flatMap((p) => nextInstallment(p.installments) ?? []).sort((a, b) => a.dueDate.localeCompare(b.dueDate))[0] ?? null)
const nextHint = computed(() => (next.value ? `Next payment ${fmtDate(next.value.dueDate, true)} · ${money(next.value.amount)}` : 'Nothing to pay right now'))
</script>

<template>
  <Head title="Installment plans" />

  <div class="flex flex-col gap-6">
    <PageHeader title="Installment plans" description="Payments are due every 14 days. Paying clears the oldest due installment first." />

    <div v-if="blocked" class="bg-accent text-accent-foreground flex items-start gap-3 rounded-xl p-4" role="status">
      <Lock class="mt-0.5 size-5 shrink-0" aria-hidden="true" />
      <p class="text-sm leading-6">
        <strong>New installment plans are paused.</strong>{{ ' ' }}
        <template v-if="overdue.length">Pay the {{ overdue.length === 1 ? 'overdue installment' : 'overdue installments' }} below and they unlock again at checkout.</template>
        <template v-else>You have two active plans. Finish one to start another.</template>
      </p>
    </div>

    <div class="grid gap-4 sm:grid-cols-3">
      <StatCard label="Remaining on plans" :value="money(outstanding)" :tone="overdue.length ? 'warning' : 'default'" :hint="overdue.length ? `${overdue.length} overdue · ${nextHint}` : nextHint" />
      <StatCard label="Active plans" :value="activePlans.length" :icon="CreditCard" hint="Up to two at a time" />
      <StatCard label="New plans" :value="blocked ? 'Paused' : 'Available'" hint="At checkout" />
    </div>

    <div v-if="plans.length" class="flex flex-col gap-5">
      <InstallmentPlanCard v-for="plan in currentPlans" :key="plan.id" :plan="plan" :busy-id="busy" @pay="pay" />
      <details v-if="pastPlans.length" class="group flex flex-col gap-5" :open="!currentPlans.length">
        <summary class="text-muted-foreground hover:text-foreground flex min-h-11 cursor-pointer list-none items-center gap-2 text-sm font-medium">
          <ChevronRight class="size-4 transition-transform group-open:rotate-90" aria-hidden="true" />
          Past plans ({{ pastPlans.length }})
        </summary>
        <div class="mt-2 flex flex-col gap-5">
          <InstallmentPlanCard v-for="plan in pastPlans" :key="plan.id" :plan="plan" :busy-id="busy" @pay="pay" />
        </div>
      </details>
    </div>

    <EmptyState v-else :icon="CreditCard" title="No installment plans yet" description="Choose 3, 6 or 12 installments when you buy and the schedule shows up here.">
      <Button as-child variant="outline"><Link href="/products" prefetch>Browse products</Link></Button>
    </EmptyState>
  </div>
</template>
