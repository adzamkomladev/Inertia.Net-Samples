<script setup lang="ts">
import { Head, router, useForm } from '@inertiajs/vue3'
import { ref } from 'vue'
import { TicketCheck } from '@lucide/vue'
import PageHeader from '@/components/PageHeader.vue'
import StatusBadge from '@/components/StatusBadge.vue'
import EmptyState from '@/components/EmptyState.vue'
import { Button } from '@/components/ui/button'
import { Textarea } from '@/components/ui/textarea'
import { fmtDateTime } from '@/lib/time'
import type { AdminTicketDetail, AdminTicketRow } from '@/types'

const props = defineProps<{
  tickets?: { data: AdminTicketRow[]; page: number; hasMore: boolean }
  ticket?: AdminTicketDetail | null
  filter: string
}>()

const filters = [
  { id: 'all', label: 'All' },
  { id: 'overdue', label: 'Overdue' },
  { id: 'pending', label: 'Waiting on us' },
  { id: 'waiting', label: 'Waiting on customer' },
  { id: 'done', label: 'Resolved' },
]
const setFilter = (id: string) => router.get('/admin/support', id === 'all' ? {} : { filter: id }, { preserveScroll: true, preserveState: true })

const replyForm = useForm({ message: '' })
const decideForm = useForm({ note: '' })
const busy = ref<string | null>(null)

const reply = (id: string) => {
  busy.value = id
  replyForm.post(`/admin/support/${id}/reply`, { preserveScroll: true, onFinish: () => (busy.value = null) })
}
const decide = (id: string, action: string) => {
  busy.value = id + action
  router.post(`/admin/support/${id}`, { action, note: decideForm.note }, { preserveScroll: true, onFinish: () => (busy.value = null) })
}
</script>

<template>
  <Head :title="props.ticket ? `Ticket ${props.ticket.number}` : 'Support'" />
    <Head title="Support" />

  <div class="flex flex-col gap-6">
    <PageHeader title="Complaints desk" description="SLA clocks tick per priority. Replies reset the clock and re-open resolved threads automatically.">
      <template #actions>
        <div v-if="!props.ticket" class="flex flex-wrap gap-1.5">
          <Button
            v-for="f in filters"
            :key="f.id"
            size="xs"
            :variant="props.filter === f.id ? 'default' : 'outline'"
            class="rounded-full"
            @click="setFilter(f.id)"
          >
            {{ f.label }}
          </Button>
        </div>
      </template>
    </PageHeader>

    <!-- single ticket detail -->
    <div v-if="props.ticket" class="flex flex-col gap-5">
      <a href="/admin/support" class="text-muted-foreground hover:text-foreground inline-flex w-fit items-center gap-1.5 text-sm">← inbox</a>
      <section class="bg-card shadow-card rounded-2xl border p-5">
        <div class="flex flex-wrap items-center gap-x-3 gap-y-1.5">
          <span class="font-mono font-semibold">{{ props.ticket.number }}</span>
          <StatusBadge :status="props.ticket.status" kind="ticket" />
          <StatusBadge :status="props.ticket.priority" kind="priority" />
          <span class="text-muted-foreground ml-auto text-xs">SLA due {{ fmtDateTime(props.ticket.dueAt) }}</span>
        </div>
        <h2 class="mt-2 text-lg font-medium">{{ props.ticket.subject }}</h2>
        <p class="text-muted-foreground mt-0.5 font-mono text-xs">
          {{ props.ticket.category.replace('_', ' ') }}<span v-if="props.ticket.orderNumber"> · order {{ props.ticket.orderNumber }}</span> ·
          opened {{ fmtDateTime(props.ticket.createdAt) }}
        </p>

        <ol class="mt-4 space-y-2 border-t pt-4">
          <li
            v-for="m in props.ticket.messages"
            :key="m.id"
            class="rounded-xl px-3 py-2 text-sm"
            :class="m.isStaff ? 'bg-primary/8 ml-10 border border-primary/15' : 'bg-muted/50 mr-10'"
          >
            <p class="text-muted-foreground mb-1 text-[11px] font-medium">
              {{ m.isStaff ? 'Support (you)' : (props.ticket.customerName ?? props.ticket.guestEmail ?? 'Customer') }} · {{ fmtDateTime(m.at) }}
            </p>
            <p class="leading-6">{{ m.body }}</p>
          </li>
        </ol>

        <form v-if="props.ticket.status !== 'closed' && props.ticket.status !== 'resolved'" class="mt-4 border-t pt-4" @submit.prevent="reply(props.ticket!.id)">
          <Textarea v-model="replyForm.message" rows="3" required maxlength="1500" placeholder="Answer the customer — replies re-open resolved tickets." />
          <Button type="submit" size="sm" class="mt-2 w-fit" :disabled="busy === props.ticket.id">Send reply</Button>
        </form>

        <div v-if="props.ticket.status !== 'closed'" class="mt-3 flex flex-wrap items-center gap-2 border-t pt-3">
          <Button size="xs" variant="outline" class="rounded-full" :disabled="busy === props.ticket.id" @click="decide(props.ticket.id, 'resolve')">
            Resolve
          </Button>
          <Button size="xs" variant="outline" class="rounded-full" :disabled="props.ticket.status !== 'open'" @click="decide(props.ticket.id, 'pending')">
            Waiting on customer
          </Button>
          <Button size="xs" variant="ghost" class="text-destructive rounded-full" :disabled="busy === props.ticket.id" @click="decide(props.ticket.id, 'close')">
            Close without reply
          </Button>
          <Textarea v-model="decideForm.note" rows="1" class="w-full" maxlength="200" placeholder="Private resolution note (optional)" />
        </div>
      </section>
    </div>

    <!-- inbox -->
    <div v-else class="flex flex-col gap-3">
      <div v-for="t in props.tickets?.data ?? []" :key="t.id" class="bg-card shadow-card hover:border-primary/30 block rounded-2xl border p-4 transition-colors">
        <div class="flex flex-wrap items-center gap-x-3 gap-y-1.5">
          <span class="font-mono text-sm font-semibold">{{ t.number }}</span>
          <StatusBadge :status="t.status" kind="ticket" size="xs" />
          <StatusBadge :status="t.priority" kind="priority" size="xs" />
          <span class="text-muted-foreground ml-auto text-xs">due {{ fmtDateTime(t.dueAt) }}</span>
        </div>
        <p class="mt-1.5 truncate text-sm font-medium">{{ t.subject }}</p>
        <p class="text-muted-foreground mt-0.5 font-mono text-xs">
          {{ t.category.replace('_', ' ') }} · {{ t.customerName ?? t.guestEmail ?? 'guest' }} · {{ t.messageCount }} message(s)
        </p>
        <Button size="xs" variant="outline" class="mt-2 rounded-full" :href="`/admin/support/${t.number}`">Open thread</Button>
      </div>
      <EmptyState v-if="!props.tickets?.data?.length" :icon="TicketCheck" title="Queue is clear" description="New complaints land here with a due-by clock based on their priority." />
    </div>
  </div>
</template>
