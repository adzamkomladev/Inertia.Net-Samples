<script setup lang="ts">
import { ref } from 'vue'
import { CreditCard } from '@lucide/vue'
import PageHeader from '@/components/PageHeader.vue'
import InstallmentPlanCard from '@/components/InstallmentPlanCard.vue'
import EmptyState from '@/components/EmptyState.vue'
import StatCard from '@/components/StatCard.vue'
import { useForm } from '@inertiajs/vue3'
import { money } from '@/lib/money'
import type { PlanView } from '@/types'

const props = defineProps<{ plans: PlanView[]; blocked: boolean }>()

const busy = ref<string | null>(null)
const payForm = useForm({})
const pay = (id: string) => {
  busy.value = id
  payForm.post(`/account/credit/installments/${id}/pay`, { onFinish: () => (busy.value = null) })
}
const activePlans = props.plans.filter((p) => p.status === 'active')
const unpaidNext = props.plans
  .flatMap((p) => p.installments.filter((i) => i.status === 'due' || i.status === 'overdue'))
  .reduce((sum, i) => sum + i.amount, 0)
</script>

<template>
  <Head title="Buy now, pay small small" />
    <Head title="My payment plans" />

  <div class="flex flex-col gap-6">
    <PageHeader
      title="My payment plans"
      description="Installments are due every 14 days; a 5% plan fee is baked into the schedule. Paying clears the oldest due installment first."
    />

    <div v-if="props.blocked" class="bg-warning/10 border-warning/30 text-warning-foreground flex items-center gap-3 rounded-2xl border p-4">
      <Lock class="text-warning size-5 shrink-0" />
      <p class="text-sm leading-6">
        <strong>New financing is paused.</strong> Clear the overdue installment{{ props.plans.some((p) => p.installments.some((i) => i.status === 'overdue')) ? 's' : '' }} below and the buy-later option unlocks automatically at checkout.
      </p>
    </div>

    <div class="grid gap-4 sm:grid-cols-3">
      <StatCard label="Active plans" :value="activePlans.length" :icon="CreditCard" hint="max 2 at a time" />
      <StatCard label="Outstanding" :value="money(unpaidNext)" hint="due + overdue" />
      <StatCard label="Block" :value="props.blocked ? 'ON' : 'off'" :icon="CreditCard" hint="new plan financing" />
    </div>

    <div v-if="plans.length" class="flex flex-col gap-5">
      <div v-for="plan in plans" :key="plan.id" class="bg-card shadow-card rounded-2xl border p-5">
        <InstallmentPlanCard :plan="plan" :busy-id="busy" @pay="pay" />
      </div>
    </div>

    <EmptyState
      v-else
      :icon="CreditCard"
      title="No plans yet"
      description="Pick the 3/6/12 installment option at checkout and the schedule lands here automatically."
    />
  </div>
</template>
