<script setup lang="ts">
import { Check } from '@lucide/vue'
import { cn } from '@/lib/utils'

defineProps<{ selected: boolean; disabled?: boolean }>()
</script>

<!-- Selectable card for a radio input placed in the default slot (use class "sr-only" on the input). -->
<template>
  <label
    :class="cn(
      'relative flex min-h-14 cursor-pointer items-start gap-3 rounded-lg border p-4 text-sm transition-colors has-[:focus-visible]:ring-2 has-[:focus-visible]:ring-ring',
      selected ? 'border-primary bg-accent/60' : 'hover:bg-muted/60',
      disabled && 'cursor-not-allowed opacity-50 hover:bg-transparent',
    )"
  >
    <span :class="cn('mt-0.5 flex size-5 shrink-0 items-center justify-center rounded-full border', selected ? 'border-primary bg-primary text-primary-foreground' : 'border-input')" aria-hidden="true">
      <Check v-if="selected" class="size-3" :stroke-width="3" />
    </span>
    <span class="min-w-0 flex-1"><slot /></span>
  </label>
</template>
