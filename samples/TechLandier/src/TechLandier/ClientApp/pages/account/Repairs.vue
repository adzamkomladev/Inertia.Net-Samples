<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import { Head, Link, useForm } from '@inertiajs/vue3'
import { ArrowLeft, ChevronRight, Loader2, Plus, Wrench } from '@lucide/vue'
import PageHeader from '@/components/PageHeader.vue'
import StatusBadge from '@/components/StatusBadge.vue'
import EmptyState from '@/components/EmptyState.vue'
import FieldError from '@/components/FieldError.vue'
import SegmentedControl from '@/components/SegmentedControl.vue'
import RelativeTime from '@/components/RelativeTime.vue'
import RepairProgress from '@/components/care/RepairProgress.vue'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select'
import { Sheet, SheetContent, SheetDescription, SheetFooter, SheetHeader, SheetTitle } from '@/components/ui/sheet'
import { Textarea } from '@/components/ui/textarea'
import { money } from '@/lib/money'
import { fmtDate, fmtDateTime } from '@/lib/time'
import type { RepairRow } from '@/types'

const props = defineProps<{
  repairs: RepairRow[]
  categories: { id: string; name: string }[]
  repair?: RepairRow | null
  history?: { status: string; note: string | null; at: string }[]
  isDetail?: boolean
}>()

const isOpen = (r: RepairRow) => r.status !== 'picked_up' && r.status !== 'closed'
const view = ref<string>(props.repairs.some(isOpen) ? 'open' : 'all')
const rows = computed(() => (view.value === 'open' ? props.repairs.filter(isOpen) : props.repairs))

const bookOpen = ref(false)
const form = useForm({ deviceName: '', deviceSerial: '', problem: '', categoryId: '' })
watch(bookOpen, (v) => { if (v) form.clearErrors() })
const submit = () => form.post('/account/care/repairs', { onSuccess: () => { form.reset(); bookOpen.value = false } })
</script>

<template>
  <Head :title="props.repair ? `Repair · ${props.repair.deviceName}` : 'Repairs'" />

  <!-- detail -->
  <div v-if="props.repair" class="flex flex-col gap-6">
    <Link href="/account/care/repairs" prefetch class="text-muted-foreground hover:text-foreground inline-flex w-fit items-center gap-1.5 text-sm transition-colors">
      <ArrowLeft class="size-4" aria-hidden="true" /> All repairs
    </Link>
    <PageHeader :title="props.repair.deviceName">
      <template #description>
        <span class="price">{{ props.repair.deviceSerial || 'Serial not set' }}</span> · booked {{ fmtDate(props.repair.createdAt, true) }}
      </template>
      <template #actions><StatusBadge :status="props.repair.status" kind="repair" /></template>
    </PageHeader>

    <section class="surface space-y-6 p-5 sm:p-6">
      <RepairProgress :status="props.repair.status" />
      <dl class="grid grid-cols-2 gap-4 text-sm sm:grid-cols-3">
        <div>
          <dt class="text-muted-foreground text-xs">Quote</dt>
          <dd class="price mt-0.5 text-lg font-semibold">{{ props.repair.quotedCost != null ? money(props.repair.quotedCost) : '—' }}</dd>
          <dd v-if="props.repair.quotedCost == null" class="text-muted-foreground text-xs">We'll quote before any work</dd>
        </div>
        <div>
          <dt class="text-muted-foreground text-xs">Technician</dt>
          <dd class="mt-0.5 font-medium">{{ props.repair.technicianName ?? 'Being assigned' }}</dd>
        </div>
        <div v-if="props.repair.appointmentOn">
          <dt class="text-muted-foreground text-xs">Appointment</dt>
          <dd class="mt-0.5 font-medium">{{ fmtDate(props.repair.appointmentOn, true) }}</dd>
        </div>
      </dl>
      <div class="bg-muted/50 rounded-xl p-4">
        <p class="text-muted-foreground mb-1 text-xs font-medium">What you told us</p>
        <p class="text-sm leading-6 whitespace-pre-wrap">{{ props.repair.problem }}</p>
      </div>
    </section>

    <section v-if="props.history?.length" class="surface p-5 sm:p-6">
      <h2 class="mb-4 font-semibold">Updates</h2>
      <ol class="relative space-y-5 before:absolute before:inset-y-1 before:left-[5px] before:w-px before:bg-border">
        <li v-for="(h, i) in [...props.history].reverse()" :key="i" class="relative pl-6">
          <span :class="['absolute top-1.5 left-0 size-[11px] rounded-full border-2 border-card', i === 0 ? 'bg-primary' : 'bg-muted-foreground/40']" aria-hidden="true" />
          <div class="flex flex-wrap items-center gap-2">
            <StatusBadge :status="h.status" kind="repair" />
            <span class="text-muted-foreground text-xs">{{ fmtDateTime(h.at) }}</span>
          </div>
          <p v-if="h.note" class="mt-1.5 text-sm leading-6">{{ h.note }}</p>
        </li>
      </ol>
    </section>
  </div>

  <!-- list -->
  <div v-else class="flex flex-col gap-6">
    <PageHeader title="Repairs" description="You get a quote first and we only repair what you approve. Every status change shows up in your alerts.">
      <template #actions><Button @click="bookOpen = true"><Plus /> Book a repair</Button></template>
    </PageHeader>

    <section class="surface overflow-hidden">
      <div v-if="props.repairs.length" class="border-b p-3">
        <SegmentedControl
          v-model="view" label="Repairs shown"
          :options="[{ value: 'open', label: `In the shop · ${props.repairs.filter(isOpen).length}` }, { value: 'all', label: `All · ${props.repairs.length}` }]"
        />
      </div>
      <ul v-if="rows.length" class="divide-y">
        <li v-for="r in rows" :key="r.id">
          <Link
            :href="`/account/care/repairs/${r.id}`" prefetch
            class="hover:bg-muted/50 group grid gap-x-4 gap-y-2 p-4 transition-colors sm:grid-cols-[minmax(0,1fr)_auto_auto] sm:items-center"
          >
            <div class="flex min-w-0 items-center gap-3">
              <span class="bg-muted text-muted-foreground flex size-10 shrink-0 items-center justify-center rounded-full"><Wrench class="size-4" aria-hidden="true" /></span>
              <div class="min-w-0">
                <p class="truncate font-medium">{{ r.deviceName }}</p>
                <p class="text-muted-foreground text-xs">Booked <RelativeTime :iso="r.createdAt" /></p>
              </div>
            </div>
            <div class="flex items-center gap-3 pl-13 sm:pl-0">
              <RepairProgress :status="r.status" compact />
              <StatusBadge :status="r.status" kind="repair" />
            </div>
            <div class="flex items-center justify-end gap-3">
              <span v-if="r.quotedCost != null" class="price text-sm font-medium">{{ money(r.quotedCost) }}</span>
              <ChevronRight class="text-muted-foreground hidden size-4 transition-transform group-hover:translate-x-0.5 sm:block" aria-hidden="true" />
            </div>
          </Link>
        </li>
      </ul>
      <EmptyState v-else :icon="Wrench" :title="props.repairs.length ? 'Nothing in the shop' : 'No repairs yet'" description="Book a repair and follow the quote, technician and progress here.">
        <Button variant="outline" @click="bookOpen = true"><Plus /> Book a repair</Button>
      </EmptyState>
    </section>
  </div>

  <Sheet v-model:open="bookOpen">
    <SheetContent class="w-full gap-0 sm:max-w-md">
      <SheetHeader class="border-b">
        <SheetTitle>Book a repair</SheetTitle>
        <SheetDescription>Drop the device at the store; we inspect it and send a quote before any work starts.</SheetDescription>
      </SheetHeader>
      <form id="book-repair" class="flex-1 space-y-5 overflow-y-auto p-4" novalidate @submit.prevent="submit">
        <div class="space-y-2">
          <Label for="r-device">Device</Label>
          <Input id="r-device" v-model="form.deviceName" maxlength="60" placeholder="e.g. HaloEdge Studio Pro A4" :aria-invalid="!!form.errors.deviceName" />
          <FieldError :message="form.errors.deviceName" />
        </div>
        <div class="grid gap-5 sm:grid-cols-2">
          <div class="space-y-2">
            <Label for="r-serial">Serial <span class="text-muted-foreground font-normal">(optional)</span></Label>
            <Input id="r-serial" v-model="form.deviceSerial" maxlength="40" placeholder="SN-…" class="price" />
          </div>
          <div v-if="categories.length" class="space-y-2">
            <Label for="r-cat">Type <span class="text-muted-foreground font-normal">(optional)</span></Label>
            <Select v-model="form.categoryId">
              <SelectTrigger id="r-cat" class="w-full"><SelectValue placeholder="Choose…" /></SelectTrigger>
              <SelectContent>
                <SelectItem v-for="c in categories" :key="c.id" :value="c.id">{{ c.name }}</SelectItem>
              </SelectContent>
            </Select>
          </div>
        </div>
        <div class="space-y-2">
          <Label for="r-problem">What's wrong</Label>
          <Textarea id="r-problem" v-model="form.problem" rows="5" maxlength="800" placeholder="Symptoms, when it started, anything already tried." :aria-invalid="!!form.errors.problem" />
          <div class="flex gap-2">
            <FieldError :message="form.errors.problem" />
            <span class="text-muted-foreground ml-auto text-xs tabular-nums">{{ form.problem.length }}/800</span>
          </div>
        </div>
      </form>
      <SheetFooter class="flex-row justify-end border-t">
        <Button type="button" variant="ghost" @click="bookOpen = false">Cancel</Button>
        <Button type="submit" form="book-repair" :disabled="form.processing || !form.isDirty">
          <Loader2 v-if="form.processing" class="animate-spin" aria-hidden="true" /> Book repair
        </Button>
      </SheetFooter>
    </SheetContent>
  </Sheet>
</template>
