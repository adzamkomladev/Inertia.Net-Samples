<script setup lang="ts">
import { computed } from 'vue'
import { statusLook, type Tone } from '@/lib/status'
import { cn } from '@/lib/utils'

const props = withDefaults(
  defineProps<{ status: string; kind?: string; class?: string }>(),
  { kind: 'order' },
)

const tones: Record<Tone, string> = {
  primary: 'bg-primary/10 text-primary dark:bg-primary/15 border-primary/20',
  success: 'bg-success/12 text-success border-success/25 dark:text-success',
  warning: 'bg-warning/15 text-warning border-warning/30',
  destructive: 'bg-destructive/10 text-destructive border-destructive/25',
  info: 'bg-chart-3/12 text-chart-3 border-chart-3/25',
  lime: 'bg-instock/20 text-foreground border-instock/40',
  neutral: 'bg-muted text-muted-foreground border-border',
}

const look = computed(() => statusLook(props.kind, props.status))
</script>

<!-- One badge for every status-ish value; the tone map keeps colors consistent app-wide. -->
<template>
  <span
    :class="
      cn(
        'inline-flex items-center gap-1 rounded-full border px-2 py-0.5 text-xs leading-4 font-medium whitespace-nowrap',
        tones[look.tone],
        props.class,
      )
    "
  >
    <span v-if="look.tone === 'success'" class="bg-success size-1.5 rounded-full" aria-hidden="true" />
    {{ look.label }}
  </span>
</template>
