<script setup lang="ts">
import { money } from '@/lib/money'
import { PLAN_COUNTS, PLAN_FEE, perInstallment } from '@/lib/installments'

defineProps<{ amount: number }>()
const longest = PLAN_COUNTS[PLAN_COUNTS.length - 1]!
const feePercent = Math.round(PLAN_FEE * 100)
</script>

<!-- "or from $X every 2 weeks, incl. 5% fee": installment pricing up front, using the longest plan, with the fee named. -->
<template>
  <p class="text-muted-foreground text-xs leading-5">
    or from <span class="price text-foreground font-medium">{{ money(perInstallment(amount, longest)) }}</span> every 2 weeks, incl. {{ feePercent }}% fee
  </p>
</template>
