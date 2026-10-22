<script setup lang="ts">
import { Check } from '@lucide/vue'
import { cn } from '@/lib/utils'

const props = defineProps<{ status: string }>()

const steps = [
  { key: 'pending', label: 'Ordered' },
  { key: 'paid', label: 'Paid' },
  { key: 'processing', label: 'Packing' },
  { key: 'shipped', label: 'On the way' },
  { key: 'delivered', label: 'Delivered' },
]
const current = () => steps.findIndex((s) => s.key === props.status)
</script>

<!-- Five-stage tracker: done steps are filled, the current one is outlined. Cancelled orders hide it. -->
<template>
  <ol class="grid grid-cols-5" :aria-label="`Order progress: ${steps[current()]?.label ?? props.status}`">
    <li v-for="(s, i) in steps" :key="s.key" class="relative flex flex-col items-center gap-2 text-center" :aria-current="i === current() ? 'step' : undefined">
      <span v-if="i > 0" :class="cn('absolute top-3.5 right-1/2 h-0.5 w-full -translate-y-1/2', i <= current() ? 'bg-primary' : 'bg-border')" aria-hidden="true" />
      <span
        :class="cn('relative flex size-7 items-center justify-center rounded-full border-2 text-xs', i < current() || (i === current() && s.key === 'delivered') ? 'border-primary bg-primary text-primary-foreground' : i === current() ? 'border-primary bg-card' : 'border-border bg-card')"
        aria-hidden="true"
      >
        <Check v-if="i < current() || (i === current() && s.key === 'delivered')" class="size-3.5" :stroke-width="3" />
        <span v-else-if="i === current()" class="bg-primary size-2 rounded-full" />
      </span>
      <span :class="cn('text-xs sm:text-sm', i <= current() ? 'font-medium' : 'text-muted-foreground')">{{ s.label }}</span>
    </li>
  </ol>
</template>
