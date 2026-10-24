<script setup lang="ts">
import { computed, ref } from 'vue'
import { Head, router, useForm } from '@inertiajs/vue3'
import { BadgeCheck, Droplets, FileWarning, Loader2, ShieldPlus, Smartphone, Zap } from '@lucide/vue'
import PageHeader from '@/components/PageHeader.vue'
import StatusBadge from '@/components/StatusBadge.vue'
import EmptyState from '@/components/EmptyState.vue'
import FieldError from '@/components/FieldError.vue'
import CoverageMeter from '@/components/care/CoverageMeter.vue'
import { Button } from '@/components/ui/button'
import { Label } from '@/components/ui/label'
import { RadioGroup, RadioGroupItem } from '@/components/ui/radio-group'
import { Textarea } from '@/components/ui/textarea'
import { Sheet, SheetContent, SheetDescription, SheetFooter, SheetHeader, SheetTitle } from '@/components/ui/sheet'
import { money } from '@/lib/money'
import type { InsuranceView } from '@/types'

defineProps<{
  policies: InsuranceView[]
  attachable: { orderItemId: string; itemName: string; product: string; price: number }[]
}>()

const INCIDENTS = [
  { value: 'damage', label: 'Damage', hint: 'Drops, cracks, liquid', icon: Droplets },
  { value: 'theft', label: 'Theft', hint: 'Stolen or robbed', icon: FileWarning },
  { value: 'malfunction', label: 'Malfunction', hint: 'Stopped working', icon: Zap },
]

const claimFor = ref<InsuranceView | null>(null)
const claimForm = useForm({ incidentType: 'damage', description: '' })
const claimOpen = computed({ get: () => claimFor.value !== null, set: (v) => { if (!v) claimFor.value = null } })
function startClaim(p: InsuranceView) {
  claimForm.reset()
  claimForm.clearErrors()
  claimFor.value = p
}
const fileClaim = () =>
  claimForm.post(`/account/care/insurance/${claimFor.value!.id}/claims`, { preserveScroll: true, onSuccess: () => (claimFor.value = null) })

const attaching = ref<string | null>(null)
const attach = (orderItemId: string) => {
  attaching.value = orderItemId
  router.post(`/account/care/insurance/attach/${orderItemId}`, {}, { preserveScroll: true, onFinish: () => (attaching.value = null) })
}
</script>

<template>
  <Head title="Device insurance" />

  <div class="flex flex-col gap-6">
    <PageHeader title="Device insurance" description="15% of the device price covers 12 months of damage, theft or malfunction. Approved claims pay 90% of the price to your mobile money." />

    <TransitionGroup
      v-if="policies.length" tag="ul" class="grid gap-3 sm:grid-cols-2 xl:grid-cols-3"
      enter-active-class="transition duration-300 ease-(--ease-out-soft)" enter-from-class="opacity-0 translate-y-1"
    >
      <li v-for="p in policies" :key="p.id" class="surface hover:shadow-raised flex flex-col gap-4 p-4 transition-shadow" :class="p.status !== 'active' && 'opacity-75'">
        <div class="flex items-start gap-3">
          <span class="bg-accent text-accent-foreground flex size-10 shrink-0 items-center justify-center rounded-full"><Smartphone class="size-4" aria-hidden="true" /></span>
          <div class="min-w-0 flex-1">
            <p class="line-clamp-2 font-medium">{{ p.deviceName }}</p>
            <p class="text-muted-foreground price text-xs">{{ p.deviceSerial || 'Serial not set' }}</p>
          </div>
          <StatusBadge :status="p.status" kind="insurance" />
        </div>
        <dl class="grid grid-cols-2 gap-3 text-sm">
          <div class="bg-muted/50 rounded-lg p-2.5">
            <dt class="text-muted-foreground text-xs">Covers up to</dt>
            <dd class="price font-semibold">{{ money(p.coverageAmount) }}</dd>
          </div>
          <div class="bg-muted/50 rounded-lg p-2.5">
            <dt class="text-muted-foreground text-xs">Premium</dt>
            <dd class="price font-semibold">{{ money(p.premium) }}<span class="text-muted-foreground text-xs font-normal">/yr</span></dd>
          </div>
        </dl>
        <CoverageMeter :start-on="p.startOn" :end-on="p.endOn" :active="p.status === 'active'" />
        <Button v-if="p.status === 'active'" variant="outline" size="sm" class="mt-auto w-full" @click="startClaim(p)"><FileWarning /> File a claim</Button>
      </li>
    </TransitionGroup>
    <div v-else class="surface">
      <EmptyState :icon="BadgeCheck" title="No policies yet" description="Insure an eligible delivered device below, or tick insurance at checkout." />
    </div>

    <section v-if="attachable.length" class="surface overflow-hidden">
      <div class="border-b p-4">
        <h2 class="flex items-center gap-2 font-semibold"><ShieldPlus class="size-4" aria-hidden="true" /> Add cover to a device you own</h2>
        <p class="text-muted-foreground mt-0.5 text-sm">Delivered devices that are still eligible for insurance.</p>
      </div>
      <TransitionGroup tag="ul" class="divide-y" leave-active-class="transition duration-200" leave-to-class="opacity-0 translate-x-4">
        <li v-for="a in attachable" :key="a.orderItemId" class="flex flex-wrap items-center gap-3 p-4">
          <div class="min-w-0 flex-1">
            <p class="truncate text-sm font-medium">{{ a.itemName }}</p>
            <p class="text-muted-foreground price text-xs">{{ money(a.price * 0.15) }}/yr · covers {{ money(a.price * 0.9) }}</p>
          </div>
          <Button size="sm" variant="outline" :disabled="attaching !== null" @click="attach(a.orderItemId)">
            <Loader2 v-if="attaching === a.orderItemId" class="animate-spin" aria-hidden="true" /><ShieldPlus v-else aria-hidden="true" /> Insure
          </Button>
        </li>
      </TransitionGroup>
    </section>
  </div>

  <Sheet v-model:open="claimOpen">
    <SheetContent class="w-full gap-0 sm:max-w-md">
      <SheetHeader class="border-b">
        <SheetTitle>Insurance claim</SheetTitle>
        <SheetDescription>{{ claimFor?.deviceName }} · approved claims pay {{ money(claimFor?.coverageAmount) }} to your mobile money.</SheetDescription>
      </SheetHeader>
      <form id="insurance-claim" class="flex-1 space-y-5 overflow-y-auto p-4" novalidate @submit.prevent="fileClaim">
        <fieldset class="space-y-2">
          <legend class="mb-2 text-sm font-medium">What happened?</legend>
          <RadioGroup v-model="claimForm.incidentType" class="grid grid-cols-3 gap-2">
            <Label
              v-for="i in INCIDENTS" :key="i.value" :for="`incident-${i.value}`"
              class="hover:bg-muted/60 has-[[data-state=checked]]:border-primary has-[[data-state=checked]]:bg-accent has-[:focus-visible]:ring-ring has-[:focus-visible]:ring-2 flex cursor-pointer flex-col items-center gap-1.5 rounded-xl border p-3 text-center font-normal transition-colors active:scale-[0.98]"
            >
              <RadioGroupItem :id="`incident-${i.value}`" :value="i.value" class="sr-only" />
              <component :is="i.icon" class="size-5" aria-hidden="true" />
              <span class="text-sm font-medium">{{ i.label }}</span>
              <span class="text-muted-foreground text-[11px] leading-4">{{ i.hint }}</span>
            </Label>
          </RadioGroup>
          <FieldError :message="claimForm.errors.incidentType" />
        </fieldset>
        <div class="space-y-2">
          <Label for="incident-desc">Describe the incident</Label>
          <Textarea id="incident-desc" v-model="claimForm.description" rows="5" maxlength="800" placeholder="What happened, when and where — the more specific, the faster the review." :aria-invalid="!!claimForm.errors.description" />
          <div class="flex gap-2">
            <FieldError :message="claimForm.errors.description" />
            <span class="text-muted-foreground ml-auto text-xs tabular-nums">{{ claimForm.description.length }}/800</span>
          </div>
        </div>
      </form>
      <SheetFooter class="flex-row justify-end border-t">
        <Button type="button" variant="ghost" @click="claimOpen = false">Cancel</Button>
        <Button type="submit" form="insurance-claim" :disabled="claimForm.processing || !claimForm.description.trim()">
          <Loader2 v-if="claimForm.processing" class="animate-spin" aria-hidden="true" /> Submit claim
        </Button>
      </SheetFooter>
    </SheetContent>
  </Sheet>
</template>
