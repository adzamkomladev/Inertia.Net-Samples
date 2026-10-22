<script setup lang="ts">
import { ref } from 'vue'
import { Head, useForm } from '@inertiajs/vue3'
import { LifeBuoy, MessageSquare } from '@lucide/vue'
import PageHeader from '@/components/PageHeader.vue'
import StatusBadge from '@/components/StatusBadge.vue'
import EmptyState from '@/components/EmptyState.vue'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select'
import { Textarea } from '@/components/ui/textarea'
import { fmtDateTime } from '@/lib/time'
import type { AccountTicketRow } from '@/types'

defineProps<{
  tickets: AccountTicketRow[]
  categories: string[]
}>()

const form = useForm({
  subject: '',
  category: 'product_fault',
  message: '',
  orderNumber: '',
})

const replyForm = useForm({ message: '' })
const replying = ref<string | null>(null)
</script>

<template>
  <Head title="Support" />

  <div class="flex flex-col gap-6">
    <PageHeader
      title="Complaints & questions"
      description="Every ticket has a priority and a response deadline. A reply reopens a resolved ticket."
    />

    <div class="grid gap-5 lg:grid-cols-[1fr_22rem]">
      <div class="flex flex-col gap-4">
        <div v-if="tickets.length" class="flex flex-col gap-4">
          <article v-for="t in tickets" :key="t.id" class="surface p-5">
            <div class="flex flex-wrap items-center gap-x-3 gap-y-1.5">
              <span class="tabular-nums text-sm font-semibold">{{ t.number }}</span>
              <StatusBadge :status="t.status" kind="ticket" />
              <StatusBadge :status="t.priority" kind="priority" />
              <span class="text-muted-foreground ml-auto text-xs">opened {{ fmtDateTime(t.createdAt) }}</span>
            </div>
            <h2 class="mt-2 text-[15px] font-medium">{{ t.subject }}</h2>
            <p class="text-muted-foreground mt-0.5 tabular-nums text-xs">
              category: {{ t.category.replace('_', ' ') }}<span v-if="t.orderNumber"> · order {{ t.orderNumber }}</span>
              · due {{ fmtDateTime(t.dueAt) }}
            </p>

            <!-- thread -->
            <ol class="mt-3 space-y-2 border-t pt-3">
              <li
                v-for="m in t.messages"
                :key="m.id"
                class="rounded-xl px-3 py-2 text-sm"
                :class="m.isStaff ? 'bg-primary/8 ml-8 border border-primary/15' : 'bg-muted/50 mr-8'"
              >
                <p class="text-muted-foreground mb-1 text-[11px] font-medium">{{ m.isStaff ? 'TechLandier support' : 'You' }} · {{ fmtDateTime(m.at) }}</p>
                <p class="leading-6">{{ m.body }}</p>
              </li>
            </ol>

            <!-- reply -->
            <div v-if="t.status !== 'closed'" class="mt-3">
              <Button size="xs" variant="outline" class="rounded-full" @click="replying = replying === t.id ? null : t.id">
                <MessageSquare class="size-3.5" /> {{ replying === t.id ? 'Close' : 'Reply' }}
              </Button>
              <form
                v-if="replying === t.id"
                class="mt-2 flex flex-col gap-2"
                @submit.prevent="replyForm.post(`/account/support/${t.id}/reply`, { onFinish: () => (replyForm.message = '') })"
              >
                <Textarea v-model="replyForm.message" rows="3" required maxlength="1200" placeholder="Anything the team should know…" />
                <Button type="submit" size="sm" :disabled="replyForm.processing" class="w-fit">Send reply</Button>
              </form>
            </div>
          </article>
        </div>
        <EmptyState v-else :icon="LifeBuoy" title="No tickets yet" description="Wrong item, late delivery, warranty question — open one on the right and it lands in the queue with an SLA." />
      </div>

      <!-- new ticket -->
      <aside>
        <section class="surface sticky top-20 p-5">
          <h2 class="font-display flex items-center gap-2 text-base font-semibold"><LifeBuoy class="size-4" /> Open a ticket</h2>
          <form class="mt-4 flex flex-col gap-3" @submit.prevent="form.post('/account/support', { onFinish: () => form.reset() })">
            <div class="grid gap-1.5">
              <Label for="s-subject">Subject</Label>
              <Input id="s-subject" v-model="form.subject" required minlength="4" maxlength="120" placeholder="One line that says the problem" />
              <span v-if="form.errors.subject" class="text-destructive text-xs">{{ form.errors.subject }}</span>
            </div>
            <div class="grid gap-1.5">
              <Label for="s-cat">Category</Label>
              <Select v-model="form.category">
                <SelectTrigger id="s-cat"><SelectValue /></SelectTrigger>
                <SelectContent>
                  <SelectItem v-for="c in categories" :key="c" :value="c">{{ c.replace('_', ' ') }}</SelectItem>
                </SelectContent>
              </Select>
            </div>
            <div class="grid gap-1.5">
              <Label for="s-order">Order number (optional)</Label>
              <Input id="s-order" v-model="form.orderNumber" maxlength="16" placeholder="TL-2026-…" />
            </div>
            <div class="grid gap-1.5">
              <Label for="s-msg">What happened</Label>
              <Textarea id="s-msg" v-model="form.message" rows="4" required maxlength="1500" placeholder="Details, photos descriptions, what you expect from us." />
              <span v-if="form.errors.message" class="text-destructive text-xs">{{ form.errors.message }}</span>
            </div>
            <Button type="submit" :disabled="form.processing" class="w-fit">Open ticket</Button>
          </form>
        </section>
      </aside>
    </div>
  </div>
</template>
