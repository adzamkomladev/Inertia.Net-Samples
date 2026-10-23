<script setup lang="ts">
import { cn } from '@/lib/utils'

defineProps<{ options: { value: string | number; label: string }[]; label: string }>()
const model = defineModel<string | number>({ required: true })
</script>

<!-- Compact single-choice control (date ranges, sorts). Buttons with aria-pressed, 44px touch height on mobile. -->
<template>
  <div role="group" :aria-label="label" class="bg-muted inline-flex rounded-full p-1">
    <button
      v-for="o in options" :key="o.value" type="button" :aria-pressed="model === o.value"
      :class="cn('h-10 shrink-0 rounded-full px-4 text-sm font-medium whitespace-nowrap transition-colors md:h-8', model === o.value ? 'bg-card shadow-sm' : 'text-muted-foreground hover:text-foreground')"
      @click="model = o.value"
    >
      {{ o.label }}
    </button>
  </div>
</template>
