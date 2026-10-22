<script setup lang="ts">
import { Head, Link } from '@inertiajs/vue3'
import PageHeader from '@/components/PageHeader.vue'
import InstallmentPlanCard from '@/components/InstallmentPlanCard.vue'
import type { PlanView } from '@/types'
defineProps<{
  detail: {
    orderNumber: string
    customer: string
    email: string
    plan: PlanView
  }
}>()
</script>
<template>
  <Head title="Installment details" />
  <div class="space-y-6">
    <Link href="/admin/credit" class="text-primary hover:underline"
      >&larr; All installment plans</Link
    >
    <PageHeader
      title="Installment details"
      :description="`${detail.customer} · ${detail.email}`"
    />
    <Link
      :href="`/admin/orders/${detail.orderNumber}`"
      class="text-primary underline"
      >View order {{ detail.orderNumber }}</Link
    >
    <InstallmentPlanCard
      :plan="detail.plan"
      :order-number="detail.orderNumber"
      :show-pay="false"
    />
  </div>
</template>
