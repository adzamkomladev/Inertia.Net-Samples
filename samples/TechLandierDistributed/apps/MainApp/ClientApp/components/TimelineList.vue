<script setup lang="ts">
import { fmtDateTime } from '@/lib/time'
import { statusLook } from '@/lib/status'
import { cn } from '@/lib/utils'
import type { OrderEventRow } from '@/types'

// Newest first reads best on a phone; the dot of the latest event is filled.
defineProps<{ events: OrderEventRow[] }>()
</script>

<!-- Order timeline: every status change with its note and time. -->
<template>
  <ol v-if="events.length" class="relative space-y-6 pl-7 before:absolute before:top-2 before:bottom-2 before:left-[5px] before:w-px before:bg-border">
    <li v-for="(event, i) in [...events].reverse()" :key="event.id" class="relative">
      <span :class="cn('absolute top-1.5 -left-7 size-[11px] rounded-full border-2', i === 0 ? (event.status === 'cancelled' ? 'border-destructive bg-destructive' : 'border-primary bg-primary') : 'border-border bg-card')" aria-hidden="true" />
      <p class="text-sm font-medium">{{ statusLook('order', event.status).label }}</p>
      <time class="text-muted-foreground text-xs" :datetime="event.at">{{ fmtDateTime(event.at) }}</time>
      <p v-if="event.note" class="text-muted-foreground mt-1 text-sm leading-5">{{ event.note }}</p>
    </li>
  </ol>
  <p v-else class="text-muted-foreground text-sm">No updates yet.</p>
</template>
