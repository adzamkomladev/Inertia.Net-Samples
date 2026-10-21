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

const hasDiscount = computed(() => props.discountPercent > 0 || (props.compareAt !== null && props.compareAt > props.price))
</script>

<!-- Mono price with struck compare-at and a discount chip. -->
<template>
  <div class="flex flex-wrap items-baseline gap-x-2 gap-y-1">
    <span :class="cn('price font-semibold', size === 'lg' ? 'text-3xl' : size === 'sm' ? 'text-sm' : 'text-lg')">
      {{ money(price) }}
    </span>
    <span v-if="hasDiscount && compareAt" class="price text-muted-foreground text-sm line-through">
      {{ money(compareAt) }}
    </span>
    <span
      v-if="discountPercent > 0"
      class="bg-brand text-brand-foreground rounded-full px-1.5 py-0.5 text-[11px] leading-none font-semibold"
    >
      −{{ discountPercent }}%
    </span>
  </div>
</template>
