<script setup lang="ts">
import { money } from '@/lib/money'
import { PLAN_COUNTS, perInstallment } from '@/lib/installments'

defineProps<{ amount: number }>()
const longest = PLAN_COUNTS[PLAN_COUNTS.length - 1]!
</script>

<!-- "or from $X every 2 weeks": installment pricing up front, using the longest plan. -->
<template>
  <p class="text-muted-foreground text-xs">
    or from <span class="price text-foreground font-medium">{{ money(perInstallment(amount, longest)) }}</span> every 2 weeks
  </p>
</template>
