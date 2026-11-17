<script setup lang="ts">
import { computed } from 'vue'
import StatusBadge from '@/components/StatusBadge.vue'
import { statusLabel } from '@/lib/status'
import { paymentLook, planOutstanding } from '@/lib/plan-labels'
import type { PlanBrief } from '@/types'

const props = defineProps<{ status: string; paymentStatus: string; plan?: PlanBrief | null }>()

const pay = computed(() => paymentLook(props.status, props.paymentStatus, props.plan))
const onPlan = computed(() => planOutstanding(props.status, props.paymentStatus, props.plan))

// "Paid" or "Delivered" twice in a row says nothing, so the payment badge only shows when it adds information.
const showPayment = computed(
  () => !!pay.value && (pay.value.kind === 'plan' || statusLabel('order', props.status) !== statusLabel('payment', props.paymentStatus)),
)
</script>

<!-- Order status plus payment state. A plan order reads "On plan · 2 of 6 paid", never a bare "Paid"; a cancelled order never reads "Unpaid". -->
<template>
  <StatusBadge :status="status" kind="order" :label="onPlan && status === 'paid' ? 'Confirmed' : undefined" />
  <StatusBadge v-if="showPayment && pay" :status="pay.status" :kind="pay.kind" :label="pay.label || undefined" />
</template>
