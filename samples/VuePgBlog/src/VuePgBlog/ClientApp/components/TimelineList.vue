<script setup lang="ts">
import { Check, CircleAlert, Minus } from '@lucide/vue'
import StatusBadge from '@/components/StatusBadge.vue'
import type { OrderEventRow } from '@/types'

defineProps<{ events: OrderEventRow[] }>()

const closed = new Set(['cancelled'])
</script>

<!-- Order timeline: every status transition with its note. -->
<template>
  <ol v-if="events.length" class="relative space-y-5 border-l pl-6">
    <li v-for="(event, i) in events" :key="event.id" class="relative">
      <span
        class="absolute top-0.5 -left-[31px] flex size-[22px] items-center justify-center rounded-full border-2"
        :class="closed.has(event.status) ? 'border-destructive/40 bg-destructive/10 text-destructive' : i === events.length - 1 ? 'border-success/50 bg-success/15 text-success' : 'border-border bg-muted text-muted-foreground'"
        aria-hidden="true"
      >
        <CircleAlert v-if="closed.has(event.status)" class="size-3" />
        <Check v-else-if="i === events.length - 1" class="size-3" />
        <Minus v-else class="size-3" />
      </span>
      <div class="flex flex-wrap items-center justify-between gap-2">
        <StatusBadge :status="event.status" kind="order" />
        <time class="text-muted-foreground text-xs" :datetime="event.at">
          {{ new Date(event.at).toLocaleString(undefined, { month: 'short', day: 'numeric', hour: '2-digit', minute: '2-digit' }) }}
        </time>
      </div>
      <p v-if="event.note" class="text-muted-foreground mt-1 text-sm leading-5">{{ event.note }}</p>
    </li>
  </ol>
  <p v-else class="text-muted-foreground text-sm">No events yet.</p>
</template>
