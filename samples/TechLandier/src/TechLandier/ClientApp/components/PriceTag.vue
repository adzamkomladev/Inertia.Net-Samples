<script setup lang="ts">
import { computed } from 'vue'
import { money } from '@/lib/money'
import { cn } from '@/lib/utils'

const props = withDefaults(
  defineProps<{
    price: number
    compareAt?: number | null
    discountPercent?: number
    size?: 'sm' | 'md' | 'lg'
  }>(),
  { compareAt: null, discountPercent: 0, size: 'md' },
)

const showCompare = computed(() => props.compareAt !== null && props.compareAt > props.price)
</script>

<!-- Price, struck compare-at and discount as plain text: no chips. -->
<template>
  <div class="flex flex-wrap items-baseline gap-x-2 gap-y-0.5">
    <span :class="cn('price font-semibold tracking-tight', size === 'lg' ? 'text-3xl' : size === 'sm' ? 'text-base' : 'text-xl')">
      {{ money(price) }}
    </span>
    <span v-if="showCompare" class="price text-muted-foreground text-sm line-through">
      <span class="sr-only">was </span>{{ money(compareAt) }}
    </span>
    <span v-if="discountPercent > 0" class="text-success text-sm font-medium">−{{ discountPercent }}%</span>
  </div>
</template>
