<script setup lang="ts">
import { computed, ref } from 'vue'
import { Head, useForm } from '@inertiajs/vue3'
import { Hash, Loader2, Pencil, ShieldCheck, Wrench } from '@lucide/vue'
import PageHeader from '@/components/PageHeader.vue'
import StatusBadge from '@/components/StatusBadge.vue'
import EmptyState from '@/components/EmptyState.vue'
import FieldError from '@/components/FieldError.vue'
import SegmentedControl from '@/components/SegmentedControl.vue'
import CoverageMeter from '@/components/care/CoverageMeter.vue'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { Textarea } from '@/components/ui/textarea'
import { Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle } from '@/components/ui/dialog'
import { Sheet, SheetContent, SheetDescription, SheetFooter, SheetHeader, SheetTitle } from '@/components/ui/sheet'
import type { WarrantyView } from '@/types'

const props = defineProps<{ warranties: WarrantyView[] }>()

const active = (w: WarrantyView) => w.status === 'active'
const view = ref<string>(props.warranties.some(active) ? 'active' : 'all')
const rows = computed(() => (view.value === 'active' ? props.warranties.filter(active) : props.warranties))

// One form per dialog, loaded from the chosen warranty (the old page shared one serial field across every card).
const serialFor = ref<WarrantyView | null>(null)
const serialForm = useForm({ serialNumber: '' })
const serialOpen = computed({ get: () => serialFor.value !== null, set: (v) => { if (!v) serialFor.value = null } })
function editSerial(w: WarrantyView) {
  serialForm.defaults({ serialNumber: w.serialNumber ?? '' })
  serialForm.reset()
  serialForm.clearErrors()
  serialFor.value = w
}
const saveSerial = () =>
  serialForm.post(`/account/care/warranties/${serialFor.value!.id}/serial`, { preserveScroll: true, onSuccess: () => (serialFor.value = null) })

const claimFor = ref<WarrantyView | null>(null)
const claimForm = useForm({ issue: '' })
const claimOpen = computed({ get: () => claimFor.value !== null, set: (v) => { if (!v) claimFor.value = null } })
function startClaim(w: WarrantyView) {
  claimForm.reset()
  claimForm.clearErrors()
  claimFor.value = w
}
const submitClaim = () =>
  claimForm.post(`/account/care/warranties/${claimFor.value!.id}/claims`, { preserveScroll: true, onSuccess: () => (claimFor.value = null) })
</script>

<template>
  <Head title="My warranties" />

  <div class="flex flex-col gap-6">
    <PageHeader title="Warranties" description="Registered automatically when an order is delivered. Add the serial number and claim while the cover is active." />

    <div v-if="warranties.length">
      <SegmentedControl
        v-model="view" label="Warranties shown"
        :options="[{ value: 'active', label: `Active · ${warranties.filter(active).length}` }, { value: 'all', label: `All · ${warranties.length}` }]"
      />
    </div>

    <TransitionGroup
      v-if="rows.length" tag="ul" class="grid gap-3 sm:grid-cols-2 xl:grid-cols-3"
      enter-active-class="transition duration-300 ease-(--ease-out-soft)" enter-from-class="opacity-0 translate-y-1"
    >
      <li v-for="w in rows" :key="w.id" class="surface hover:shadow-raised flex flex-col gap-4 p-4 transition-shadow" :class="!active(w) && 'opacity-75'">
        <div class="flex items-start gap-3">
          <span class="bg-accent text-accent-foreground flex size-10 shrink-0 items-center justify-center rounded-full"><ShieldCheck class="size-4" aria-hidden="true" /></span>
          <div class="min-w-0 flex-1">
            <p class="line-clamp-2 font-medium">{{ w.itemName }}</p>
            <button
              type="button" class="text-muted-foreground hover:text-foreground group mt-0.5 inline-flex items-center gap-1 text-xs transition-colors"
              :aria-label="w.serialNumber ? `Edit serial ${w.serialNumber}` : `Add serial for ${w.itemName}`" @click="editSerial(w)"
            >
              <Hash class="size-3" aria-hidden="true" />
              <span :class="w.serialNumber ? 'price' : 'underline underline-offset-2'">{{ w.serialNumber ?? 'Add serial number' }}</span>
              <Pencil v-if="w.serialNumber" class="size-3 opacity-0 transition-opacity group-hover:opacity-100" aria-hidden="true" />
            </button>
          </div>
          <StatusBadge :status="w.status" kind="warranty" />
        </div>
        <CoverageMeter :start-on="w.startOn" :end-on="w.endOn" :active="active(w)" />
        <Button v-if="active(w)" variant="outline" size="sm" class="mt-auto w-full" @click="startClaim(w)"><Wrench /> Make a claim</Button>
      </li>
    </TransitionGroup>
    <div v-else class="surface">
      <EmptyState :icon="ShieldCheck" :title="warranties.length ? 'No active cover' : 'No warranties yet'" description="Orders for products with warranty months register their cover here the moment they're delivered." />
    </div>
  </div>

  <Dialog v-model:open="serialOpen">
    <DialogContent class="sm:max-w-sm">
      <DialogHeader>
        <DialogTitle>Device serial</DialogTitle>
        <DialogDescription>{{ serialFor?.itemName }} — usually on the box or under Settings → About.</DialogDescription>
      </DialogHeader>
      <form id="serial-form" class="space-y-2" @submit.prevent="saveSerial">
        <Label for="serial" class="sr-only">Serial number</Label>
        <Input id="serial" v-model="serialForm.serialNumber" class="price" placeholder="SN-…" maxlength="40" autocomplete="off" :aria-invalid="!!serialForm.errors.serialNumber" />
        <FieldError :message="serialForm.errors.serialNumber" />
      </form>
      <DialogFooter>
        <Button type="button" variant="ghost" @click="serialOpen = false">Cancel</Button>
        <Button type="submit" form="serial-form" :disabled="serialForm.processing || !serialForm.isDirty">
          <Loader2 v-if="serialForm.processing" class="animate-spin" aria-hidden="true" /> Save serial
        </Button>
      </DialogFooter>
    </DialogContent>
  </Dialog>

  <Sheet v-model:open="claimOpen">
    <SheetContent class="w-full gap-0 sm:max-w-md">
      <SheetHeader class="border-b">
        <SheetTitle>Warranty claim</SheetTitle>
        <SheetDescription>{{ claimFor?.itemName }}. We review within two working days and repair or replace covered faults.</SheetDescription>
      </SheetHeader>
      <form id="claim-form" class="flex-1 space-y-2 overflow-y-auto p-4" novalidate @submit.prevent="submitClaim">
        <Label for="claim-issue">What stopped working?</Label>
        <Textarea id="claim-issue" v-model="claimForm.issue" rows="6" maxlength="800" placeholder="What happens, when it started, and anything you've already tried." :aria-invalid="!!claimForm.errors.issue" />
        <div class="flex gap-2">
          <FieldError :message="claimForm.errors.issue" />
          <span class="text-muted-foreground ml-auto text-xs tabular-nums">{{ claimForm.issue.length }}/800</span>
        </div>
        <p v-if="!claimFor?.serialNumber" class="bg-warning/10 text-warning rounded-lg p-3 text-xs">Adding the serial number first speeds up the review.</p>
      </form>
      <SheetFooter class="flex-row justify-end border-t">
        <Button type="button" variant="ghost" @click="claimOpen = false">Cancel</Button>
        <Button type="submit" form="claim-form" :disabled="claimForm.processing || !claimForm.issue.trim()">
          <Loader2 v-if="claimForm.processing" class="animate-spin" aria-hidden="true" /> Submit claim
        </Button>
      </SheetFooter>
    </SheetContent>
  </Sheet>
</template>
