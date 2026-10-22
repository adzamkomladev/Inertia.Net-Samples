<script setup lang="ts">
import { computed } from 'vue'
import { statusLook, type Tone } from '@/lib/status'
import { cn } from '@/lib/utils'

const props = withDefaults(
  defineProps<{ status: string; kind?: string; class?: string }>(),
  { kind: 'order' },
)

// Soft tinted pill plus a dot: state is never colour-only (the label always renders).
const tones: Record<Tone, { pill: string; dot: string }> = {
  primary: { pill: 'bg-accent text-accent-foreground', dot: 'bg-primary' },
  success: { pill: 'bg-success/12 text-success', dot: 'bg-success' },
  warning: { pill: 'bg-warning/14 text-warning', dot: 'bg-warning' },
  destructive: { pill: 'bg-destructive/10 text-destructive', dot: 'bg-destructive' },
  info: { pill: 'bg-accent text-accent-foreground', dot: 'bg-primary' },
  lime: { pill: 'bg-success/12 text-success', dot: 'bg-success' },
  neutral: { pill: 'bg-muted text-muted-foreground', dot: 'bg-muted-foreground/60' },
}

const look = computed(() => statusLook(props.kind, props.status))
const t = computed(() => tones[look.value.tone])
</script>

<!-- One badge for every status-ish value; the tone map keeps colours consistent app-wide. -->
<template>
  <span :class="cn('inline-flex items-center gap-1.5 rounded-full px-2.5 py-1 text-xs leading-none font-medium whitespace-nowrap', t.pill, props.class)">
    <span :class="cn('size-1.5 rounded-full', t.dot)" aria-hidden="true" />
    {{ look.label }}
  </span>
</template>
