<script setup lang="ts">
import { Check } from '@lucide/vue'
import { cn } from '@/lib/utils'

defineProps<{ steps: string[]; current: number }>()
const emit = defineEmits<{ go: [index: number] }>()
</script>

<!-- Numbered steps as an ordered list: finished steps are buttons that jump back, the current and later steps are plain items. -->
<template>
  <ol class="flex items-center gap-2 text-sm" aria-label="Checkout progress">
    <li v-for="(s, i) in steps" :key="s" class="flex items-center gap-2" :aria-current="i === current ? 'step' : undefined">
      <component
        :is="i < current ? 'button' : 'span'"
        :type="i < current ? 'button' : undefined"
        :class="cn('flex items-center gap-2 rounded-full py-1 pr-3 pl-1 font-medium transition-colors', i < current && 'hover:bg-card pointer-coarse:min-h-11', i === current ? 'bg-card' : i > current && 'text-muted-foreground')"
        @click="i < current && emit('go', i)"
      >
        <span :class="cn('flex size-6 items-center justify-center rounded-full text-xs', i <= current ? 'bg-primary text-primary-foreground' : 'bg-muted')">
          <Check v-if="i < current" class="size-3.5" :stroke-width="3" aria-label="Completed" />
          <template v-else>{{ i + 1 }}</template>
        </span>
        {{ s }}
      </component>
      <span v-if="i < steps.length - 1" class="bg-border hidden h-px w-6 sm:block" aria-hidden="true" />
    </li>
  </ol>
</template>
