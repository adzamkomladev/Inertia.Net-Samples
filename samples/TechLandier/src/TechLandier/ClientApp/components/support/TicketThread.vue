<script setup lang="ts">
import { fmtDateTime } from '@/lib/time'
import { cn } from '@/lib/utils'
import type { TicketMessageRow } from '@/types'

defineProps<{ messages: TicketMessageRow[]; staffLabel: string; customerLabel: string; mine: 'staff' | 'customer' }>()
</script>

<!-- Chat-style thread: the viewer's own messages sit on the right in the accent tone. -->
<template>
  <TransitionGroup
    tag="ol" class="flex flex-col gap-3"
    enter-active-class="transition duration-300 ease-(--ease-out-soft)" enter-from-class="opacity-0 translate-y-2"
  >
    <li
      v-for="m in messages" :key="m.id"
      :class="cn('flex max-w-[85%] flex-col gap-1', (m.isStaff ? mine === 'staff' : mine === 'customer') ? 'items-end self-end' : 'items-start')"
    >
      <p class="text-muted-foreground px-1 text-[11px]">
        <span class="text-foreground font-medium">{{ m.isStaff ? staffLabel : customerLabel }}</span> · {{ fmtDateTime(m.at) }}
      </p>
      <p
        :class="cn('rounded-2xl px-3.5 py-2.5 text-sm leading-6 whitespace-pre-wrap',
                   (m.isStaff ? mine === 'staff' : mine === 'customer') ? 'bg-primary text-primary-foreground rounded-br-md' : 'bg-muted rounded-bl-md')"
      >
        {{ m.body }}
      </p>
    </li>
  </TransitionGroup>
</template>
