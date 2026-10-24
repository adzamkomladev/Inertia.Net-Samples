<script setup lang="ts">
import { computed } from 'vue'
import { Check } from '@lucide/vue'
import { cn } from '@/lib/utils'

const props = defineProps<{ status: string; compact?: boolean }>()

const STEPS = [
  { key: 'received', label: 'Received' },
  { key: 'quoted', label: 'Quoted' },
  { key: 'repairing', label: 'Repairing' },
  { key: 'ready', label: 'Ready' },
  { key: 'picked_up', label: 'Collected' },
]
// "closed" counts as past the last step.
const at = computed(() => (props.status === 'closed' ? STEPS.length : STEPS.findIndex((s) => s.key === props.status)))
</script>

<!-- Repair journey: done steps are filled, the current one pulses gently (static under reduced motion). -->
<template>
  <ol v-if="!compact" class="grid grid-cols-5 gap-1" aria-label="Repair progress">
    <li v-for="(s, i) in STEPS" :key="s.key" class="flex flex-col gap-2" :aria-current="i === at ? 'step' : undefined">
      <span :class="cn('h-1.5 rounded-full transition-colors duration-500', i < at ? 'bg-primary' : i === at ? 'bg-primary animate-pulse-glow' : 'bg-muted')" />
      <span :class="cn('flex items-center gap-1 text-[11px] sm:text-xs', i <= at ? 'text-foreground font-medium' : 'text-muted-foreground')">
        <Check v-if="i < at" class="text-primary hidden size-3 sm:block" aria-hidden="true" />{{ s.label }}
      </span>
    </li>
  </ol>
  <span v-else class="flex items-center gap-0.5" :aria-label="`Step ${Math.min(at + 1, STEPS.length)} of ${STEPS.length}`" role="img">
    <span v-for="(s, i) in STEPS" :key="s.key" :class="cn('h-1 w-3 rounded-full', i <= at ? 'bg-primary' : 'bg-muted')" />
  </span>
</template>
