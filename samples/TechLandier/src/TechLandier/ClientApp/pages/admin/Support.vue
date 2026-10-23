<script setup lang="ts">
import { Head, Link, router, useForm } from '@inertiajs/vue3'
import { computed, ref } from 'vue'
import { ArrowLeft, CheckCheck, ChevronLeft, ChevronRight, Clock, Hourglass, Loader2, Mail, MessageSquare, Package, Send, TicketCheck, XCircle } from '@lucide/vue'
import PageHeader from '@/components/PageHeader.vue'
import StatusBadge from '@/components/StatusBadge.vue'
import EmptyState from '@/components/EmptyState.vue'
import FieldError from '@/components/FieldError.vue'
import SegmentedControl from '@/components/SegmentedControl.vue'
import RelativeTime from '@/components/RelativeTime.vue'
import SlaDue from '@/components/support/SlaDue.vue'
import TicketThread from '@/components/support/TicketThread.vue'
import { Button } from '@/components/ui/button'
import { Label } from '@/components/ui/label'
import { Textarea } from '@/components/ui/textarea'
import { fmtDateTime } from '@/lib/time'
import type { AdminTicketDetail, AdminTicketRow } from '@/types'

const props = defineProps<{
  tickets?: { data: AdminTicketRow[]; page: number; hasMore: boolean }
  ticket?: AdminTicketDetail | null
  filter: string
}>()

const FILTERS = [
  { value: 'all', label: 'All' },
  { value: 'overdue', label: 'Overdue' },
  { value: 'pending', label: 'Waiting on us' },
  { value: 'waiting', label: 'Waiting on customer' },
  { value: 'done', label: 'Resolved' },
]
const loading = ref(false)
const go = (filter: string, page = 1) =>
  router.get('/admin/support', { ...(filter === 'all' ? {} : { filter }), ...(page > 1 ? { page } : {}) }, {
    preserveScroll: true, preserveState: true,
    onStart: () => (loading.value = true), onFinish: () => (loading.value = false),
  })
const filterModel = computed({ get: () => props.filter, set: (v: string) => go(v) })

const isDone = (status: string) => status === 'resolved' || status === 'closed'
const who = (t: { customerName: string | null; guestEmail: string | null }) => t.customerName ?? t.guestEmail ?? 'Guest'

// ---------------------------------------------------------------- detail

const replyForm = useForm({ message: '' })
const decideForm = useForm({ note: '' })
const deciding = ref<string | null>(null)

function reply() {
  if (!props.ticket) return
  replyForm.post(`/admin/support/${props.ticket.id}/reply`, { preserveScroll: true, onSuccess: () => replyForm.reset() })
}
function decide(action: string) {
  if (!props.ticket) return
  deciding.value = action
  decideForm
    .transform((d) => ({ action, note: d.note }))
    .post(`/admin/support/${props.ticket.id}`, { preserveScroll: true, onSuccess: () => decideForm.reset(), onFinish: () => (deciding.value = null) })
}
</script>

<template>
  <Head :title="props.ticket ? `Ticket ${props.ticket.number}` : 'Support'" />

  <!-- single ticket -->
  <div v-if="props.ticket" class="flex flex-col gap-6">
    <Link href="/admin/support" prefetch class="text-muted-foreground hover:text-foreground inline-flex w-fit items-center gap-1.5 text-sm transition-colors">
      <ArrowLeft class="size-4" aria-hidden="true" /> Inbox
    </Link>
    <PageHeader :title="props.ticket.subject">
      <template #description>
        <span class="price">{{ props.ticket.number }}</span> · <span class="capitalize">{{ props.ticket.category.replace(/_/g, ' ') }}</span> · opened <RelativeTime :iso="props.ticket.createdAt" />
      </template>
      <template #actions>
        <StatusBadge :status="props.ticket.status" kind="ticket-admin" />
        <StatusBadge :status="props.ticket.priority" kind="priority" />
      </template>
    </PageHeader>

    <div class="grid gap-5 lg:grid-cols-[minmax(0,1fr)_20rem] lg:items-start">
      <section class="surface flex flex-col overflow-hidden">
        <div class="p-4 sm:p-5">
          <TicketThread :messages="props.ticket.messages" staff-label="Support" :customer-label="who(props.ticket)" mine="staff" />
        </div>
        <form v-if="props.ticket.status !== 'closed'" class="bg-muted/30 space-y-2 border-t p-4" @submit.prevent="reply">
          <Label for="admin-reply" class="sr-only">Reply to the customer</Label>
          <Textarea
            id="admin-reply" v-model="replyForm.message" rows="3" maxlength="1500" class="bg-card" :aria-invalid="!!replyForm.errors.message"
            :placeholder="isDone(props.ticket.status) ? 'Replying re-opens this resolved ticket…' : `Reply to ${who(props.ticket)}…`"
            @keydown.ctrl.enter="reply" @keydown.meta.enter="reply"
          />
          <div class="flex items-center gap-2">
            <FieldError :message="replyForm.errors.message" />
            <span class="text-muted-foreground ml-auto hidden text-xs sm:inline">Ctrl + Enter to send</span>
            <Button type="submit" size="sm" :disabled="replyForm.processing || !replyForm.message.trim()">
              <Loader2 v-if="replyForm.processing" class="animate-spin" aria-hidden="true" /><Send v-else aria-hidden="true" /> Send reply
            </Button>
          </div>
        </form>
      </section>

      <aside class="flex flex-col gap-4 lg:sticky lg:top-6">
        <section class="surface space-y-3 p-4 text-sm">
          <h2 class="font-semibold">Details</h2>
          <dl class="space-y-2.5">
            <div class="flex items-center justify-between gap-3"><dt class="text-muted-foreground flex items-center gap-1.5"><Clock class="size-3.5" aria-hidden="true" />Response due</dt><dd><SlaDue :due-at="props.ticket.dueAt" :done="isDone(props.ticket.status)" /></dd></div>
            <div class="flex items-center justify-between gap-3"><dt class="text-muted-foreground flex items-center gap-1.5"><MessageSquare class="size-3.5" aria-hidden="true" />First response</dt><dd>{{ props.ticket.firstResponseAt ? fmtDateTime(props.ticket.firstResponseAt) : '—' }}</dd></div>
            <div v-if="props.ticket.orderNumber" class="flex items-center justify-between gap-3">
              <dt class="text-muted-foreground flex items-center gap-1.5"><Package class="size-3.5" aria-hidden="true" />Order</dt>
              <dd><Link :href="`/admin/orders/${props.ticket.orderNumber}`" class="text-primary price hover:underline">{{ props.ticket.orderNumber }}</Link></dd>
            </div>
          </dl>
          <div class="border-t pt-3">
            <p class="font-medium">{{ who(props.ticket) }}</p>
            <a v-if="props.ticket.customerEmail ?? props.ticket.guestEmail" :href="`mailto:${props.ticket.customerEmail ?? props.ticket.guestEmail}`" class="text-muted-foreground hover:text-foreground flex items-center gap-1.5 text-xs break-all">
              <Mail class="size-3.5 shrink-0" aria-hidden="true" />{{ props.ticket.customerEmail ?? props.ticket.guestEmail }}
            </a>
          </div>
        </section>

        <section v-if="props.ticket.status !== 'closed'" class="surface space-y-3 p-4">
          <h2 class="text-sm font-semibold">Update ticket</h2>
          <Label for="decide-note" class="sr-only">Private note</Label>
          <Textarea id="decide-note" v-model="decideForm.note" rows="2" maxlength="200" placeholder="Private note (optional)" />
          <FieldError :message="decideForm.errors.note ?? (decideForm.errors as Record<string, string>).action" />
          <div class="grid gap-2">
            <Button size="sm" :disabled="deciding !== null || isDone(props.ticket.status)" @click="decide('resolve')">
              <Loader2 v-if="deciding === 'resolve'" class="animate-spin" aria-hidden="true" /><CheckCheck v-else aria-hidden="true" /> Mark resolved
            </Button>
            <Button size="sm" variant="outline" :disabled="deciding !== null || props.ticket.status !== 'open'" @click="decide('pending')">
              <Loader2 v-if="deciding === 'pending'" class="animate-spin" aria-hidden="true" /><Hourglass v-else aria-hidden="true" /> Waiting on customer
            </Button>
            <Button size="sm" variant="ghost" class="text-destructive hover:text-destructive" :disabled="deciding !== null" @click="decide('close')">
              <Loader2 v-if="deciding === 'close'" class="animate-spin" aria-hidden="true" /><XCircle v-else aria-hidden="true" /> Close without reply
            </Button>
          </div>
        </section>
      </aside>
    </div>
  </div>

  <!-- inbox -->
  <div v-else class="flex flex-col gap-6">
    <PageHeader title="Support" description="Most urgent first: overdue tickets lead, then by response deadline. A reply re-opens a resolved ticket." />

    <section class="surface overflow-hidden">
      <div class="border-b p-3">
        <SegmentedControl v-model="filterModel" label="Ticket filter" class="scrollbar-none max-w-full overflow-x-auto" :options="FILTERS" />
      </div>
      <ul v-if="props.tickets?.data?.length" class="divide-y transition-opacity" :class="loading && 'opacity-60'">
        <li v-for="t in props.tickets.data" :key="t.id">
          <Link
            :href="`/admin/support/${t.number}`" prefetch
            class="hover:bg-muted/50 group grid gap-x-4 gap-y-1.5 p-4 transition-colors md:grid-cols-[minmax(0,1fr)_12rem_9rem_auto] md:items-center"
          >
            <div class="min-w-0 space-y-1">
              <div class="flex flex-wrap items-center gap-2">
                <span class="price text-muted-foreground text-xs font-medium">{{ t.number }}</span>
                <StatusBadge :status="t.status" kind="ticket-admin" />
                <StatusBadge :status="t.priority" kind="priority" />
              </div>
              <p class="truncate font-medium">{{ t.subject }}</p>
            </div>
            <p class="text-muted-foreground min-w-0 truncate text-xs">
              <span class="text-foreground">{{ who(t) }}</span><br class="hidden md:block"><span class="md:hidden"> · </span>
              <span class="capitalize">{{ t.category.replace(/_/g, ' ') }}</span> · {{ t.messageCount }} {{ t.messageCount === 1 ? 'message' : 'messages' }}
            </p>
            <div><SlaDue :due-at="t.dueAt" :done="isDone(t.status)" /></div>
            <ChevronRight class="text-muted-foreground hidden size-4 transition-transform group-hover:translate-x-0.5 md:block" aria-hidden="true" />
          </Link>
        </li>
      </ul>
      <EmptyState v-else :icon="TicketCheck" :title="props.filter === 'all' ? 'Queue is clear' : 'Nothing here'" description="New complaints land here with a due-by clock based on their priority." />

      <nav v-if="props.tickets && (props.tickets.page > 1 || props.tickets.hasMore)" class="flex items-center justify-between border-t p-3" aria-label="Ticket pages">
        <Button variant="ghost" size="sm" :disabled="props.tickets.page <= 1 || loading" @click="go(props.filter, props.tickets.page - 1)"><ChevronLeft /> Newer</Button>
        <span class="text-muted-foreground text-xs">Page {{ props.tickets.page }}</span>
        <Button variant="ghost" size="sm" :disabled="!props.tickets.hasMore || loading" @click="go(props.filter, props.tickets.page + 1)">Older <ChevronRight /></Button>
      </nav>
    </section>
  </div>
</template>
