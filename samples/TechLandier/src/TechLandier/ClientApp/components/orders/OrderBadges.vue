<script setup lang="ts">
import { computed } from 'vue'
import StatusBadge from '@/components/StatusBadge.vue'
import { statusLabel } from '@/lib/status'
import type { PlanBrief } from '@/types'

const props = defineProps<{ status: string; paymentStatus: string; plan?: PlanBrief | null }>()

// Cancelled or refunded orders drop the plan wording: nothing is left to pay.
const onPlan = computed(() => !!props.plan && props.status !== 'cancelled' && props.paymentStatus !== 'refunded')
const planDone = computed(() => !!props.plan && props.plan.paid >= props.plan.count)

// "Paid" twice in a row says nothing, so the payment badge only shows when it adds information.
const showPayment = computed(() => onPlan.value || statusLabel('order', props.status) !== statusLabel('payment', props.paymentStatus))
</script>

<!-- Order status plus payment state. A plan order reads "On plan · 2/6 paid", never a bare "Paid". -->
<template>
  <StatusBadge :status="status" kind="order" :label="onPlan && status === 'paid' ? 'Confirmed' : undefined" />
  <template v-if="showPayment">
    <StatusBadge v-if="onPlan && plan" :status="planDone ? 'completed' : 'onplan'" kind="plan" :label="planDone ? 'Plan paid in full' : `On plan · ${plan.paid}/${plan.count} paid`" />
    <StatusBadge v-else :status="paymentStatus" kind="payment" />
  </template>
</template>
