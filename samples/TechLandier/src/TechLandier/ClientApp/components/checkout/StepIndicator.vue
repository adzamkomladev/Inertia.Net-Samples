<script setup lang="ts">
import { Check } from '@lucide/vue'
import { cn } from '@/lib/utils'

defineProps<{ steps: string[]; current: number }>()
const emit = defineEmits<{ go: [index: number] }>()
</script>

<!-- Numbered steps; completed steps are buttons that jump back. -->
<template>
  <ol class="flex items-center gap-2 text-sm" aria-label="Checkout progress">
    <li v-for="(s, i) in steps" :key="s" class="flex items-center gap-2" :aria-current="i === current ? 'step' : undefined">
      <button
        type="button" :disabled="i >= current" :class="cn('flex items-center gap-2 rounded-full py-1 pr-3 pl-1 font-medium transition-colors', i === current ? 'bg-card' : i < current ? 'hover:bg-card' : 'text-muted-foreground')"
        @click="emit('go', i)"
      >
        <span :class="cn('flex size-6 items-center justify-center rounded-full text-xs', i <= current ? 'bg-primary text-primary-foreground' : 'bg-muted')">
          <Check v-if="i < current" class="size-3.5" :stroke-width="3" aria-label="Completed" />
          <template v-else>{{ i + 1 }}</template>
        </span>
        {{ s }}
      </button>
      <span v-if="i < steps.length - 1" class="bg-border hidden h-px w-6 sm:block" aria-hidden="true" />
    </li>
  </ol>
</template>
