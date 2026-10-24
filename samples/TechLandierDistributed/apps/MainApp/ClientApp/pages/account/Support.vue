<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import { Head, useForm } from '@inertiajs/vue3'
import { ChevronRight, LifeBuoy, Loader2, MessageSquare, Plus, Search, Send } from '@lucide/vue'
import PageHeader from '@/components/PageHeader.vue'
import StatusBadge from '@/components/StatusBadge.vue'
import EmptyState from '@/components/EmptyState.vue'
import FieldError from '@/components/FieldError.vue'
import SegmentedControl from '@/components/SegmentedControl.vue'
import RelativeTime from '@/components/RelativeTime.vue'
import SlaDue from '@/components/support/SlaDue.vue'
import TicketThread from '@/components/support/TicketThread.vue'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select'
import { Sheet, SheetContent, SheetDescription, SheetFooter, SheetHeader, SheetTitle } from '@/components/ui/sheet'
import { Textarea } from '@/components/ui/textarea'
import type { AccountTicketRow } from '@/types'

const props = defineProps<{
  tickets: AccountTicketRow[]
  categories: string[]
}>()

const label = (s: string) => s.replace(/_/g, ' ')
const isDone = (t: AccountTicketRow) => t.status === 'resolved' || t.status === 'closed'

// ---------------------------------------------------------------- list

const view = ref<string>('open')
const query = ref('')
const counts = computed(() => ({ open: props.tickets.filter((t) => !isDone(t)).length, done: props.tickets.filter(isDone).length }))
const rows = computed(() => {
  const q = query.value.trim().toLowerCase()
  return props.tickets.filter((t) =>
    (view.value === 'all' || (view.value === 'open') !== isDone(t))
    && (!q || [t.number, t.subject, t.orderNumber ?? '', label(t.category)].some((v) => v.toLowerCase().includes(q))))
})
const lastMessage = (t: AccountTicketRow) => t.messages.at(-1)

// ---------------------------------------------------------------- thread sheet

const openId = ref<string | null>(null)
// Read from props so the thread refreshes after a reply or a realtime reload.
const active = computed(() => props.tickets.find((t) => t.id === openId.value) ?? null)
const threadOpen = computed({ get: () => active.value !== null, set: (v) => { if (!v) openId.value = null } })

const replyForm = useForm({ message: '' })
watch(openId, () => { replyForm.reset(); replyForm.clearErrors() })
function reply() {
  if (!active.value) return
  replyForm.post(`/account/support/${active.value.id}/reply`, { preserveScroll: true, onSuccess: () => replyForm.reset() })
}

// ---------------------------------------------------------------- new ticket sheet

const newOpen = ref(false)
const form = useForm({ subject: '', category: 'product_fault', message: '', orderNumber: '' })
watch(newOpen, (v) => { if (v) form.clearErrors() })
function create() {
  form.post('/account/support', {
    preserveScroll: true,
    onSuccess: () => { form.reset(); newOpen.value = false; view.value = 'open' },
  })
}
</script>

<template>
  <Head title="Support" />

  <div class="flex flex-col gap-6">
    <PageHeader title="Support" description="Every ticket has a priority and a response deadline. A reply reopens a resolved ticket.">
      <template #actions>
        <Button @click="newOpen = true"><Plus /> New ticket</Button>
      </template>
    </PageHeader>

    <section class="surface overflow-hidden">
      <div class="flex flex-col gap-3 border-b p-3 sm:flex-row sm:items-center">
        <SegmentedControl
          v-model="view" label="Ticket status"
          :options="[
            { value: 'open', label: `Open · ${counts.open}` },
            { value: 'done', label: `Resolved · ${counts.done}` },
            { value: 'all', label: 'All' },
          ]"
        />
        <div class="relative sm:ml-auto sm:w-64">
          <Search class="text-muted-foreground pointer-events-none absolute top-1/2 left-3 size-4 -translate-y-1/2" aria-hidden="true" />
          <Input v-model="query" type="search" placeholder="Ticket, subject, order…" class="pl-9" aria-label="Search tickets" />
        </div>
      </div>

      <TransitionGroup
        v-if="rows.length" tag="ul" class="divide-y"
        enter-active-class="transition duration-300 ease-(--ease-out-soft)" enter-from-class="opacity-0 -translate-y-1"
      >
        <li v-for="t in rows" :key="t.id">
          <button
            type="button"
            class="hover:bg-muted/50 focus-visible:bg-muted/50 group grid w-full gap-x-4 gap-y-1.5 p-4 text-left transition-colors sm:grid-cols-[minmax(0,1fr)_auto] sm:items-center"
            :aria-label="`Open ticket ${t.number}: ${t.subject}`"
            @click="openId = t.id"
          >
            <div class="min-w-0 space-y-1">
              <div class="flex flex-wrap items-center gap-2">
                <span class="price text-muted-foreground text-xs font-medium">{{ t.number }}</span>
                <StatusBadge :status="t.status" kind="ticket" />
                <StatusBadge v-if="t.priority === 'high' || t.priority === 'urgent'" :status="t.priority" kind="priority" />
              </div>
              <p class="truncate font-medium">{{ t.subject }}</p>
              <p class="text-muted-foreground flex items-center gap-1.5 truncate text-xs">
                <MessageSquare class="size-3.5 shrink-0" aria-hidden="true" />
                <span class="truncate">{{ lastMessage(t)?.isStaff ? 'Support: ' : 'You: ' }}{{ lastMessage(t)?.body }}</span>
              </p>
            </div>
            <div class="flex items-center justify-between gap-3 sm:justify-end">
              <span class="text-muted-foreground text-xs capitalize">{{ label(t.category) }}<template v-if="t.orderNumber"> · {{ t.orderNumber }}</template></span>
              <SlaDue :due-at="t.dueAt" :done="isDone(t)" />
              <ChevronRight class="text-muted-foreground hidden size-4 transition-transform group-hover:translate-x-0.5 sm:block" aria-hidden="true" />
            </div>
          </button>
        </li>
      </TransitionGroup>
      <EmptyState
        v-else :icon="query ? Search : LifeBuoy"
        :title="query ? 'No tickets match' : view === 'open' ? 'Nothing open' : 'No tickets yet'"
        :description="query ? 'Try a ticket number, subject or order.' : 'Wrong item, late delivery, warranty question — open a ticket and it lands in the queue with a response deadline.'"
      >
        <Button v-if="!query" variant="outline" @click="newOpen = true"><Plus /> New ticket</Button>
      </EmptyState>
    </section>
  </div>

  <!-- conversation -->
  <Sheet v-model:open="threadOpen">
    <SheetContent class="w-full gap-0 sm:max-w-lg">
      <template v-if="active">
        <SheetHeader class="border-b pr-12">
          <div class="flex flex-wrap items-center gap-2">
            <span class="price text-muted-foreground text-xs font-medium">{{ active.number }}</span>
            <StatusBadge :status="active.status" kind="ticket" />
            <StatusBadge :status="active.priority" kind="priority" />
          </div>
          <SheetTitle class="text-lg">{{ active.subject }}</SheetTitle>
          <SheetDescription class="flex flex-wrap items-center gap-x-2 gap-y-1">
            <span class="capitalize">{{ label(active.category) }}</span>
            <span v-if="active.orderNumber">· order {{ active.orderNumber }}</span>
            <span>· opened <RelativeTime :iso="active.createdAt" /></span>
            <SlaDue :due-at="active.dueAt" :done="isDone(active)" />
          </SheetDescription>
        </SheetHeader>

        <div class="flex-1 overflow-y-auto p-4">
          <TicketThread :messages="active.messages" staff-label="TechLandier support" customer-label="You" mine="customer" />
        </div>

        <SheetFooter class="border-t">
          <form v-if="active.status !== 'closed'" class="w-full space-y-2" @submit.prevent="reply">
            <Label for="ticket-reply" class="sr-only">Reply</Label>
            <Textarea
              id="ticket-reply" v-model="replyForm.message" rows="3" maxlength="1200" :aria-invalid="!!replyForm.errors.message"
              :placeholder="isDone(active) ? 'Replying reopens this ticket…' : 'Anything the team should know…'"
              @keydown.ctrl.enter="reply" @keydown.meta.enter="reply"
            />
            <div class="flex items-center gap-2">
              <FieldError :message="replyForm.errors.message" />
              <span class="text-muted-foreground ml-auto hidden text-xs sm:inline">Ctrl + Enter to send</span>
              <Button type="submit" size="sm" :disabled="replyForm.processing || !replyForm.message.trim()">
                <Loader2 v-if="replyForm.processing" class="animate-spin" aria-hidden="true" /><Send v-else aria-hidden="true" /> Send
              </Button>
            </div>
          </form>
          <p v-else class="text-muted-foreground text-sm">This ticket is closed. Open a new one if you still need help.</p>
        </SheetFooter>
      </template>
    </SheetContent>
  </Sheet>

  <!-- new ticket -->
  <Sheet v-model:open="newOpen">
    <SheetContent class="w-full gap-0 sm:max-w-md">
      <SheetHeader class="border-b">
        <SheetTitle>Open a ticket</SheetTitle>
        <SheetDescription>We reply within the response window for its priority — usually the same day.</SheetDescription>
      </SheetHeader>
      <form id="new-ticket" class="flex-1 space-y-5 overflow-y-auto p-4" novalidate @submit.prevent="create">
        <div class="space-y-2">
          <Label for="s-subject">Subject</Label>
          <Input id="s-subject" v-model="form.subject" maxlength="120" placeholder="One line that says the problem" :aria-invalid="!!form.errors.subject" />
          <FieldError :message="form.errors.subject" />
        </div>
        <div class="grid gap-5 sm:grid-cols-2">
          <div class="space-y-2">
            <Label for="s-cat">Category</Label>
            <Select v-model="form.category">
              <SelectTrigger id="s-cat" class="w-full capitalize"><SelectValue /></SelectTrigger>
              <SelectContent>
                <SelectItem v-for="c in categories" :key="c" :value="c" class="capitalize">{{ label(c) }}</SelectItem>
              </SelectContent>
            </Select>
          </div>
          <div class="space-y-2">
            <Label for="s-order">Order <span class="text-muted-foreground font-normal">(optional)</span></Label>
            <Input id="s-order" v-model="form.orderNumber" maxlength="16" placeholder="TL-2026-…" :aria-invalid="!!form.errors.orderNumber" />
            <FieldError :message="form.errors.orderNumber" />
          </div>
        </div>
        <div class="space-y-2">
          <Label for="s-msg">What happened</Label>
          <Textarea id="s-msg" v-model="form.message" rows="6" maxlength="1500" placeholder="Details, what you've tried, and what you'd like us to do." :aria-invalid="!!form.errors.message" />
          <div class="flex gap-2">
            <FieldError :message="form.errors.message" />
            <span class="text-muted-foreground ml-auto text-xs tabular-nums">{{ form.message.length }}/1500</span>
          </div>
        </div>
      </form>
      <SheetFooter class="flex-row justify-end border-t">
        <Button type="button" variant="ghost" @click="newOpen = false">Cancel</Button>
        <Button type="submit" form="new-ticket" :disabled="form.processing || !form.isDirty">
          <Loader2 v-if="form.processing" class="animate-spin" aria-hidden="true" /> Open ticket
        </Button>
      </SheetFooter>
    </SheetContent>
  </Sheet>
</template>
