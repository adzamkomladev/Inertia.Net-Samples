<script setup lang="ts">
import type { Component } from 'vue'
import { Bell, CreditCard, Mail, PackageCheck, ShieldCheck, Wrench } from '@lucide/vue'
import RelativeTime from '@/components/RelativeTime.vue'
import type { AlertRow } from '@/types'

defineProps<{ alerts: AlertRow[]; unreadCount?: number }>()

const emit = defineEmits<{ read: [id: string] }>()

const kinds: Record<string, { icon: Component; class: string }> = {
  order_status: { icon: PackageCheck, class: 'bg-chart-3/15 text-chart-3' },
  installment_due: { icon: CreditCard, class: 'bg-warning/15 text-warning' },
  overdue: { icon: CreditCard, class: 'bg-destructive/12 text-destructive' },
  reply: { icon: Mail, class: 'bg-brand/12 text-brand' },
  insurance: { icon: ShieldCheck, class: 'bg-success/12 text-success' },
  warranty: { icon: ShieldCheck, class: 'bg-success/12 text-success' },
  repair: { icon: Wrench, class: 'bg-accent text-accent-foreground' },
  promo: { icon: Bell, class: 'bg-brand/12 text-brand' },
}
const look = (kind: string) => kinds[kind] ?? { icon: Bell, class: 'bg-muted text-muted-foreground' }
</script>

<!-- Notification inbox rows with per-row mark-as-read. -->
<template>
  <ul class="divide-y overflow-hidden rounded-2xl border shadow-xs">
    <li
      v-for="alert in alerts"
      :key="alert.id"
      class="bg-card flex gap-3.5 px-4 py-4 transition-colors sm:px-5"
      :class="alert.readAt ? '' : 'bg-accent/40 dark:bg-accent/25'"
    >
      <span :class="look(alert.kind).class" class="flex size-9 shrink-0 items-center justify-center rounded-xl" aria-hidden="true">
        <component :is="look(alert.kind).icon" class="size-4" :stroke-width="1.75" />
      </span>
      <div class="min-w-0 flex-1">
        <div class="flex flex-wrap items-center justify-between gap-x-3 gap-y-1">
          <p class="text-sm font-medium" :class="alert.readAt ? '' : 'font-semibold'">{{ alert.title }}</p>
          <RelativeTime :iso="alert.createdAt" class="text-muted-foreground shrink-0 text-xs" />
        </div>
        <p class="text-muted-foreground mt-0.5 text-sm leading-5">{{ alert.body }}</p>
      </div>
      <button
        v-if="!alert.readAt"
        type="button"
        class="text-muted-foreground hover:text-foreground h-fit rounded-full border px-2.5 py-1 text-xs transition-colors hover:bg-muted"
        @click="emit('read', alert.id)"
      >
        Mark read
      </button>
    </li>
  </ul>
</template>
