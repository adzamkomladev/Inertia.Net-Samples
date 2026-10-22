<script setup lang="ts">
import { computed } from 'vue'
import { Head, router } from '@inertiajs/vue3'
import { BellOff, CheckCheck, Gift, Package, ShieldCheck, Wrench, CreditCard, MessageSquare, BadgeCheck } from '@lucide/vue'
import PageHeader from '@/components/PageHeader.vue'
import EmptyState from '@/components/EmptyState.vue'
import { Button } from '@/components/ui/button'
import { fmtDateTime } from '@/lib/time'
import type { AlertRow } from '@/types'

const props = defineProps<{ alerts: { data: AlertRow[]; page: number; hasMore: boolean }; unreadCount: number }>()
const rows = computed(() => props.alerts?.data ?? [])

const kindIcon: Record<string, any> = {
  order_status: Package,
  installment_due: CreditCard,
  overdue: CreditCard,
  knowledge: Gift,
  reply: MessageSquare,
  insurance: ShieldCheck,
  warranty: BadgeCheck,
  repair: Wrench,
  promo: Gift,
}

const open = (alert: AlertRow) => {
  if (!alert.readAt) router.post(`/account/alerts/${alert.id}/read`, {}, { preserveScroll: true })
  if (alert.href) router.get(alert.href)
}
</script>

<template>
  <Head title="Alerts" />

  <div class="flex flex-col gap-6">
    <PageHeader title="Alerts" description="Order updates, payment reminders, claim decisions and replies.">
      <template #actions>
        <Button v-if="unreadCount" size="sm" variant="outline" class="rounded-full" @click="router.post('/account/alerts/read-all', {}, { preserveScroll: true })">
          <CheckCheck class="size-3.5" /> Mark all read
        </Button>
      </template>
    </PageHeader>

    <div v-if="rows.length" class="flex flex-col gap-2">
      <button
        v-for="a in rows"
        :key="a.id"
        class="surface hover:bg-muted/50 flex items-start gap-3 p-4 text-left transition-colors"
        :class="!a.readAt && 'border-primary/25'"
        @click="open(a)"
      >
        <span class="bg-accent text-accent-foreground grid size-9 shrink-0 place-items-center rounded-xl">
          <component :is="kindIcon[a.kind] ?? BellOff" class="size-4" :stroke-width="1.75" aria-hidden="true" />
        </span>
        <span class="min-w-0 flex-1">
          <span class="flex flex-wrap items-center gap-2">
            <span class="text-sm font-semibold">{{ a.title }}</span>
            <span v-if="!a.readAt" class="bg-primary/15 text-primary rounded-full px-1.5 py-0.5 text-[10px] font-semibold">new</span>
          </span>
          <span class="text-muted-foreground mt-0.5 block text-[13px] leading-5">{{ a.body }}</span>
          <span class="text-muted-foreground/70 mt-1 block text-[11px]">{{ fmtDateTime(a.createdAt) }}</span>
        </span>
      </button>
    </div>

    <EmptyState v-else :icon="BellOff" title="Inbox zero" description="Order progress, payment reminders and claim decisions will show up here the moment they happen." />
  </div>
</template>
