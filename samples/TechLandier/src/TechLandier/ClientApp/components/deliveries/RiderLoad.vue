<script setup lang="ts">
import { computed } from 'vue'
import { cn } from '@/lib/utils'

const props = defineProps<{ active: number; capacity: number; compact?: boolean }>()

const ratio = computed(() => Math.min(1, props.active / Math.max(1, props.capacity)))
const tone = computed(() => (ratio.value >= 1 ? 'bg-destructive' : ratio.value >= 0.75 ? 'bg-warning' : 'bg-success'))
</script>

<!-- Rider workload against capacity: one meter plus the numbers, so load is never colour-only. -->
<template>
  <div :class="cn('flex items-center gap-2', compact ? 'w-24' : 'w-full')" :title="`${active} of ${capacity} runs`">
    <div
      class="bg-muted h-1.5 flex-1 overflow-hidden rounded-full" role="meter"
      :aria-valuenow="active" aria-valuemin="0" :aria-valuemax="capacity" :aria-label="`Load ${active} of ${capacity}`"
    >
      <div :class="cn('h-full rounded-full transition-[width] duration-500 ease-(--ease-out-soft)', tone)" :style="{ width: `${ratio * 100}%` }" />
    </div>
    <span class="price text-muted-foreground shrink-0 text-xs tabular-nums">{{ active }}/{{ capacity }}</span>
  </div>
</template>
