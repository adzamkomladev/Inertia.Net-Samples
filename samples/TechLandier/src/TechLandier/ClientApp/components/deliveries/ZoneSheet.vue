<script setup lang="ts">
import { watch } from 'vue'
import { useForm } from '@inertiajs/vue3'
import { Loader2 } from '@lucide/vue'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { Switch } from '@/components/ui/switch'
import { Sheet, SheetContent, SheetDescription, SheetFooter, SheetHeader, SheetTitle } from '@/components/ui/sheet'
import FieldError from '@/components/FieldError.vue'
import type { ZoneAdminRow } from '@/types'

const props = defineProps<{ zone: ZoneAdminRow | null }>()
const open = defineModel<boolean>('open', { required: true })

const ETA_PRESETS = ['Same day if ordered by 2pm', 'Next business day', '1–2 days', '2–4 days']

const blank = { name: '', fee: 5, etaText: ETA_PRESETS[0]!, allowPayOnDelivery: true, isActive: true }
const form = useForm({ ...blank })

watch(open, (isOpen) => {
  if (!isOpen) return
  form.clearErrors()
  const z = props.zone
  form.defaults(z ? { name: z.name, fee: z.fee, etaText: z.etaText, allowPayOnDelivery: z.allowPayOnDelivery, isActive: z.isActive } : { ...blank })
  form.reset()
})

function save() {
  form.post(props.zone ? `/admin/zones/${props.zone.id}` : '/admin/zones', {
    preserveScroll: true,
    onSuccess: () => (open.value = false),
  })
}
</script>

<template>
  <Sheet v-model:open="open">
    <SheetContent class="w-full gap-0 sm:max-w-md">
      <SheetHeader class="border-b">
        <SheetTitle>{{ zone ? `Edit ${zone.name}` : 'Add a delivery zone' }}</SheetTitle>
        <SheetDescription>Customers see the fee and ETA at checkout when they pick this zone.</SheetDescription>
      </SheetHeader>

      <form id="zone-form" class="flex-1 space-y-5 overflow-y-auto p-4" novalidate @submit.prevent="save">
        <div class="space-y-2">
          <Label for="zone-name">Zone name</Label>
          <Input id="zone-name" v-model="form.name" maxlength="60" autocomplete="off" placeholder="e.g. East Legon" :aria-invalid="!!form.errors.name" />
          <FieldError :message="form.errors.name" />
        </div>
        <div class="space-y-2">
          <Label for="zone-fee">Delivery fee</Label>
          <div class="relative">
            <span class="text-muted-foreground pointer-events-none absolute inset-y-0 left-3 flex items-center text-sm">$</span>
            <Input id="zone-fee" v-model.number="form.fee" type="number" inputmode="decimal" min="0" step="0.5" class="pl-7" :aria-invalid="!!form.errors.fee" />
          </div>
          <FieldError :message="form.errors.fee" />
        </div>
        <div class="space-y-2">
          <Label for="zone-eta">Estimated delivery</Label>
          <Input id="zone-eta" v-model="form.etaText" maxlength="64" placeholder="Same day if ordered by 2pm" :aria-invalid="!!form.errors.etaText" />
          <div class="flex flex-wrap gap-1.5" role="group" aria-label="ETA suggestions">
            <button
              v-for="p in ETA_PRESETS" :key="p" type="button"
              :aria-pressed="form.etaText === p"
              class="bg-muted text-muted-foreground hover:text-foreground aria-pressed:bg-accent aria-pressed:text-accent-foreground rounded-full px-2.5 py-1 text-xs transition-colors active:scale-95"
              @click="form.etaText = p"
            >
              {{ p }}
            </button>
          </div>
          <FieldError :message="form.errors.etaText" />
        </div>
        <div class="divide-y rounded-xl border">
          <label for="zone-cash" class="hover:bg-muted/50 flex cursor-pointer items-start justify-between gap-4 p-4 transition-colors">
            <span class="space-y-0.5">
              <span class="block text-sm font-medium">Cash on delivery</span>
              <span class="text-muted-foreground block text-xs">Riders collect the order total at the door.</span>
            </span>
            <Switch id="zone-cash" v-model="form.allowPayOnDelivery" />
          </label>
          <label for="zone-active" class="hover:bg-muted/50 flex cursor-pointer items-start justify-between gap-4 p-4 transition-colors">
            <span class="space-y-0.5">
              <span class="block text-sm font-medium">Offered at checkout</span>
              <span class="text-muted-foreground block text-xs">Turn off to pause new orders to this zone.</span>
            </span>
            <Switch id="zone-active" v-model="form.isActive" />
          </label>
        </div>
      </form>

      <SheetFooter class="flex-row justify-end border-t">
        <Button type="button" variant="ghost" @click="open = false">Cancel</Button>
        <Button type="submit" form="zone-form" :disabled="form.processing || !form.isDirty && !!zone">
          <Loader2 v-if="form.processing" class="animate-spin" aria-hidden="true" />
          {{ zone ? 'Save changes' : 'Add zone' }}
        </Button>
      </SheetFooter>
    </SheetContent>
  </Sheet>
</template>
