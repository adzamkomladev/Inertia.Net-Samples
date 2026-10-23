<script setup lang="ts">
import { computed } from 'vue'
import { useIntervalFn, useNow } from '@vueuse/core'
import { AlarmClock, CheckCircle2 } from '@lucide/vue'
import RelativeTime from '@/components/RelativeTime.vue'
import { cn } from '@/lib/utils'

const props = defineProps<{ dueAt: string; done?: boolean }>()

// Re-evaluated each minute so a ticket flips to overdue while the page stays open.
const now = useNow({ scheduler: (cb) => useIntervalFn(cb, 60_000) })
const left = computed(() => new Date(props.dueAt).getTime() - now.value.getTime())
const tone = computed(() =>
  props.done ? 'text-muted-foreground' : left.value < 0 ? 'bg-destructive/10 text-destructive' : left.value < 86_400_000 ? 'bg-warning/14 text-warning' : 'text-muted-foreground')
</script>

<!-- SLA clock: overdue and due-today states get a tint; the words always say which. -->
<template>
  <span :class="cn('inline-flex items-center gap-1 rounded-full text-xs whitespace-nowrap', tone, !done && left < 86_400_000 && 'px-2 py-0.5 font-medium')">
    <CheckCircle2 v-if="done" class="size-3.5" aria-hidden="true" />
    <AlarmClock v-else class="size-3.5" aria-hidden="true" />
    <template v-if="done">Answered</template>
    <template v-else>{{ left < 0 ? 'Overdue' : 'Due' }} <RelativeTime :iso="dueAt" /></template>
  </span>
</template>
