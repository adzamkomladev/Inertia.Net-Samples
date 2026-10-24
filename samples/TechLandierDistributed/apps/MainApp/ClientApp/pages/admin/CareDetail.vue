<script setup lang="ts">
import { computed } from 'vue'
import { Head, Link, useForm } from '@inertiajs/vue3'
import { ArrowLeft, Loader2 } from '@lucide/vue'
import RepairProgress from '@/components/care/RepairProgress.vue'
import PageHeader from '@/components/PageHeader.vue'
import StatusBadge from '@/components/StatusBadge.vue'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { Textarea } from '@/components/ui/textarea'
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/components/ui/select'
import { money } from '@/lib/money'
import { fmtDateTime } from '@/lib/time'
const props = defineProps<{
  detail: {
    id: string
    kind: string
    device: string
    customer: string
    email: string
    serial: string | null
    description: string
    status: string
    createdAt: string
    updatedAt: string
    orderNumber: string | null
    startOn: string | null
    endOn: string | null
    coverage: number | null
    amount: number | null
    note: string | null
    technician: string | null
    appointmentOn: string | null
    history: { status: string; note: string | null; at: string }[]
  }
}>()
const isRepair = computed(() => props.detail.kind === 'repairs')
const isInsurance = computed(() => props.detail.kind === 'insurance-claims')
const title = computed(() =>
  isRepair.value
    ? 'Repair details'
    : isInsurance.value
      ? 'Insurance claim details'
      : 'Warranty claim details',
)
const form = useForm({
  decision: 'review',
  payout: props.detail.amount ?? props.detail.coverage ?? 0,
  note: '',
  status: props.detail.status,
  quotedCost: props.detail.amount ?? 0,
  technicianName: props.detail.technician ?? '',
})
const actions = computed(() => {
  const s = props.detail.status
  if (isInsurance.value)
    return s === 'submitted' || s === 'under_review'
      ? ['review', 'approve', 'reject']
      : s === 'approved'
        ? ['pay', 'reject']
        : []
  return s === 'submitted' || s === 'in_review'
    ? ['review', 'approve', 'reject']
    : s === 'approved'
      ? ['ship', 'resolve', 'reject']
      : s === 'replacement_shipped'
        ? ['resolve']
        : []
})
const submit = (decision?: string) => {
  if (decision) form.decision = decision
  form.post(`/admin/care/${props.detail.kind}/${props.detail.id}`, {
    preserveScroll: true,
    onSuccess: () => form.reset('note'),
  })
}
</script>
<template>
  <Head :title="title" />
  <div class="space-y-6">
    <Link
      :href="`/admin/care?tab=${isRepair ? 'repairs' : isInsurance ? 'insurance' : 'warranties'}`"
      class="text-muted-foreground hover:text-foreground inline-flex w-fit items-center gap-1.5 text-sm transition-colors"
      ><ArrowLeft class="size-4" aria-hidden="true" /> Care desk</Link
    >
    <PageHeader :title="title" :description="detail.device"
      ><template #actions
        ><StatusBadge
          :status="detail.status"
          :kind="
            isRepair ? 'repair' : isInsurance ? 'insurance' : 'warranty'
          " /></template
    ></PageHeader>
    <div class="grid gap-6 lg:grid-cols-[1fr_22rem]">
      <div class="space-y-6">
        <section class="surface space-y-4 p-6">
          <RepairProgress v-if="isRepair" :status="detail.status" class="mb-2" />
          <h2 class="font-semibold">
            {{ isRepair ? 'Reported problem' : 'Claim report' }}
          </h2>
          <p class="whitespace-pre-wrap text-sm leading-7">
            {{ detail.description }}
          </p>
          <dl class="grid gap-4 text-sm sm:grid-cols-2">
            <div>
              <dt class="text-muted-foreground">Device serial</dt>
              <dd>{{ detail.serial || 'Not supplied' }}</dd>
            </div>
            <div>
              <dt class="text-muted-foreground">Opened</dt>
              <dd>{{ fmtDateTime(detail.createdAt) }}</dd>
            </div>
            <div>
              <dt class="text-muted-foreground">Last updated</dt>
              <dd>{{ fmtDateTime(detail.updatedAt) }}</dd>
            </div>
            <div v-if="detail.startOn">
              <dt class="text-muted-foreground">Coverage dates</dt>
              <dd>{{ detail.startOn }} &ndash; {{ detail.endOn }}</dd>
            </div>
            <div v-if="detail.coverage != null">
              <dt class="text-muted-foreground">Coverage limit</dt>
              <dd>{{ money(detail.coverage) }}</dd>
            </div>
            <div v-if="detail.amount != null">
              <dt class="text-muted-foreground">
                {{ isRepair ? 'Quote' : 'Payout' }}
              </dt>
              <dd>{{ money(detail.amount) }}</dd>
            </div>
            <div v-if="detail.technician">
              <dt class="text-muted-foreground">Technician</dt>
              <dd>{{ detail.technician }}</dd>
            </div>
            <div v-if="detail.appointmentOn">
              <dt class="text-muted-foreground">Appointment</dt>
              <dd>{{ detail.appointmentOn }}</dd>
            </div>
          </dl>
          <p v-if="detail.note" class="rounded-lg bg-muted p-3 text-sm">
            {{ detail.note }}
          </p>
        </section>
        <section v-if="detail.history.length" class="surface space-y-3 p-6">
          <h2 class="font-semibold">Repair history</h2>
          <ol class="space-y-4">
            <li
              v-for="(event, index) in detail.history"
              :key="index"
              class="border-l-2 pl-4 text-sm"
            >
              <p class="font-medium">{{ event.status.replaceAll('_', ' ') }}</p>
              <p class="text-muted-foreground">{{ fmtDateTime(event.at) }}</p>
              <p class="mt-1 whitespace-pre-wrap">{{ event.note }}</p>
            </li>
          </ol>
        </section>
      </div>
      <aside class="space-y-6">
        <section class="surface space-y-2 p-6">
          <h2 class="font-semibold">Customer</h2>
          <p>{{ detail.customer }}</p>
          <a
            :href="`mailto:${detail.email}`"
            class="break-all text-sm text-primary underline"
            >{{ detail.email }}</a
          ><Link
            v-if="detail.orderNumber"
            :href="`/admin/orders/${detail.orderNumber}`"
            class="block text-sm text-primary underline"
            >Order {{ detail.orderNumber }}</Link
          >
        </section>
        <form
          v-if="isRepair || actions.length"
          class="surface space-y-4 p-6"
          @submit.prevent="submit()"
        >
          <h2 class="font-semibold">
            {{ isRepair ? 'Update repair' : 'Review and decide' }}
          </h2>
          <template v-if="isRepair">
            <Label for="repair-status">Status</Label
            ><Select v-model="form.status"
              ><SelectTrigger id="repair-status"><SelectValue /></SelectTrigger
              ><SelectContent
                ><SelectItem
                  v-for="s in [
                    'received',
                    'quoted',
                    'repairing',
                    'ready',
                    'picked_up',
                    'closed',
                  ]"
                  :key="s"
                  :value="s"
                  >{{ s.replaceAll('_', ' ') }}</SelectItem
                ></SelectContent
              ></Select
            >
            <Label for="quote">Quoted cost</Label
            ><Input
              id="quote"
              v-model="form.quotedCost"
              type="number"
              min="0"
              step="0.01"
            />
            <Label for="technician">Technician</Label
            ><Input
              id="technician"
              v-model="form.technicianName"
              maxlength="60"
            />
          </template>
          <template v-if="isInsurance && detail.status === 'approved'"
            ><Label for="payout">Payout amount</Label
            ><Input
              id="payout"
              v-model="form.payout"
              type="number"
              min="0.01"
              :max="detail.coverage ?? undefined"
              step="0.01"
          /></template>
          <Label for="case-note">Customer note</Label
          ><Textarea
            id="case-note"
            v-model="form.note"
            rows="3"
            maxlength="800"
          />
          <p
            v-for="(error, field) in form.errors"
            :key="field"
            class="text-sm text-destructive"
            role="alert"
          >
            {{ error }}
          </p>
          <Button v-if="isRepair" :disabled="form.processing || !form.isDirty" type="submit"
            ><Loader2 v-if="form.processing" class="animate-spin" aria-hidden="true" />Save repair</Button
          >
          <div v-else class="flex flex-wrap gap-2">
            <Button
              v-for="action in actions"
              :key="action"
              type="button"
              :variant="action === 'reject' ? 'destructive' : 'default'"
              :disabled="form.processing"
              @click="submit(action)"
              ><Loader2 v-if="form.processing && form.decision === action" class="animate-spin" aria-hidden="true" />{{
                action === 'pay'
                  ? 'Record payout'
                  : action === 'ship'
                    ? 'Replacement shipped'
                    : action
              }}</Button
            >
          </div>
        </form>
      </aside>
    </div>
  </div>
</template>
