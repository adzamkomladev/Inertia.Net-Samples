<script setup lang="ts">
import { Head, router, useForm, Link } from '@inertiajs/vue3'
import { Wrench } from '@lucide/vue'
import PageHeader from '@/components/PageHeader.vue'
import StatusBadge from '@/components/StatusBadge.vue'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select'
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

const form = useForm({
  deviceName: '',
  deviceSerial: '',
  problem: '',
  categoryId: '',
})

const submit = () => form.post('/account/care/repairs')
</script>

<template>
  <Head :title="props.repair ? `Repair ${props.repair.id.slice(0, 8)}` : 'Repairs'" />

  <div class="flex flex-col gap-6">
    <PageHeader
      title="Repairs"
      description="You get a quote first and we only repair what you approve. Every status change shows up in your alerts."
    />

    <div class="grid gap-5 lg:grid-cols-[1fr_22rem]">
      <div class="flex flex-col gap-5">
        <!-- detail (when opening one ticket) -->
        <section v-if="props.repair" class="surface p-5">
          <div class="flex flex-wrap items-center gap-x-3 gap-y-1.5">
            <h2 class="font-display text-base font-semibold">{{ props.repair.deviceName }}</h2>
            <StatusBadge :status="props.repair.status" kind="repair" />
            <Button as-child variant="ghost" size="sm" class="ml-auto"><Link href="/account/care/repairs" prefetch>← all repairs</Link></Button>
          </div>
          <p class="text-muted-foreground mt-1 tabular-nums text-xs">{{ props.repair.deviceSerial || 'serial not set' }} · opened {{ fmtDate(props.repair.createdAt, true) }}</p>
          <p class="mt-3 text-sm leading-6">{{ props.repair.problem }}</p>
          <dl class="mt-4 grid grid-cols-2 gap-3 text-sm">
            <div><dt class="text-muted-foreground text-xs">Quoted cost</dt><dd class="font-medium">{{ props.repair.quotedCost != null ? money(props.repair.quotedCost) : 'pending quote' }}</dd></div>
            <div><dt class="text-muted-foreground text-xs">Technician</dt><dd class="font-medium">{{ props.repair.technicianName ?? '—' }}</dd></div>
            <div v-if="props.repair.appointmentOn"><dt class="text-muted-foreground text-xs">Appointment</dt><dd class="font-medium">{{ props.repair.appointmentOn }}</dd></div>
          </dl>
          <ol v-if="props.history?.length" class="mt-4 space-y-2 border-t pt-3 text-sm">
            <li v-for="(h, i) in props.history" :key="i" class="flex gap-3">
              <StatusBadge :status="h.status" kind="repair" />
              <span class="text-muted-foreground">{{ fmtDateTime(h.at) }}</span>
              <span v-if="h.note" class="truncate">{{ h.note }}</span>
            </li>
          </ol>
        </section>

        <!-- list -->
        <section class="surface flex flex-col gap-3 p-5">
          <h2 class="font-display text-base font-semibold">My repair tickets</h2>
          <div v-if="props.repairs.length" class="flex flex-col gap-2">
            <button
              v-for="r in props.repairs"
              :key="r.id"
              class="border-border/70 hover:border-primary/40 flex flex-wrap items-center gap-x-3 gap-y-1 rounded-xl border p-3 text-left transition-colors"
              @click="router.get(`/account/care/repairs/${r.id}`, {}, { preserveScroll: true })"
            >
              <span class="text-sm font-medium">{{ r.deviceName }}</span>
              <StatusBadge :status="r.status" kind="repair" />
              <span class="text-muted-foreground ml-auto text-xs">{{ fmtDate(r.createdAt, true) }}</span>
              <span v-if="r.quotedCost != null" class="tabular-nums text-xs">{{ money(r.quotedCost) }}</span>
            </button>
          </div>
          <p v-else class="text-muted-foreground text-sm">Nothing in the shop right now.</p>
        </section>
      </div>

      <!-- create -->
      <aside>
        <section class="surface sticky top-20 p-5">
          <h2 class="font-display flex items-center gap-2 text-base font-semibold"><Wrench class="size-4" /> Book a repair</h2>
          <form class="mt-4 flex flex-col gap-3" @submit.prevent="submit">
            <div class="grid gap-1.5">
              <Label for="r-device">Device</Label>
              <Input id="r-device" v-model="form.deviceName" required maxlength="60" placeholder="e.g. HaloEdge Studio Pro A4" />
              <span v-if="form.errors.deviceName" class="text-destructive text-xs">{{ form.errors.deviceName }}</span>
            </div>
            <div class="grid gap-1.5">
              <Label for="r-serial">Serial (optional)</Label>
              <Input id="r-serial" v-model="form.deviceSerial" maxlength="40" placeholder="SN-…" />
            </div>
            <div class="grid gap-1.5">
              <Label for="r-problem">What's wrong</Label>
              <Textarea id="r-problem" v-model="form.problem" rows="3" required maxlength="800" placeholder="Symptoms, when it started, anything already tried." />
              <span v-if="form.errors.problem" class="text-destructive text-xs">{{ form.errors.problem }}</span>
            </div>
            <div v-if="categories.length" class="grid gap-1.5">
              <Label for="r-cat">Category hint</Label>
              <Select v-model="form.categoryId">
                <SelectTrigger id="r-cat"><SelectValue placeholder="Optional" /></SelectTrigger>
                <SelectContent>
                  <SelectItem :value="c.id" v-for="c in categories" :key="c.id">{{ c.name }}</SelectItem>
                </SelectContent>
              </Select>
            </div>
            <Button type="submit" :disabled="form.processing" class="w-fit">Open ticket</Button>
          </form>
        </section>
      </aside>
    </div>
  </div>
</template>
