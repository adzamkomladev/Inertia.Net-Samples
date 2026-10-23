<script setup lang="ts">
import { watch } from 'vue'
import { useForm } from '@inertiajs/vue3'
import { Loader2 } from '@lucide/vue'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { Switch } from '@/components/ui/switch'
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select'
import { Sheet, SheetContent, SheetDescription, SheetFooter, SheetHeader, SheetTitle } from '@/components/ui/sheet'
import FieldError from '@/components/FieldError.vue'
import type { RiderAdminRow } from '@/types'

const props = defineProps<{ rider: RiderAdminRow | null }>()
const open = defineModel<boolean>('open', { required: true })

const SERVICES = [
  { value: 'in_house', label: 'In-house fleet' },
  { value: 'yango', label: 'Yango' },
  { value: 'bolt', label: 'Bolt' },
  { value: 'uber', label: 'Uber' },
  { value: 'other', label: 'Other partner' },
]

const form = useForm({ name: '', phone: '', vehicle: '', service: 'in_house', capacity: 6, isActive: true })

// Opening the sheet loads the chosen rider (or a blank one); stale errors from a previous edit are dropped.
watch(open, (isOpen) => {
  if (!isOpen) return
  form.clearErrors()
  const r = props.rider
  form.defaults(r
    ? { name: r.name, phone: r.phone, vehicle: r.vehicle ?? '', service: r.service, capacity: r.capacity, isActive: r.isActive }
    : { name: '', phone: '', vehicle: '', service: 'in_house', capacity: 6, isActive: true })
  form.reset()
})

function save() {
  form.post(props.rider ? `/admin/riders/${props.rider.id}` : '/admin/riders', {
    preserveScroll: true,
    onSuccess: () => (open.value = false),
  })
}
</script>

<template>
  <Sheet v-model:open="open">
    <SheetContent class="w-full gap-0 sm:max-w-md">
      <SheetHeader class="border-b">
        <SheetTitle>{{ rider ? `Edit ${rider.name}` : 'Add a rider' }}</SheetTitle>
        <SheetDescription>Contact details, vehicle and how many runs they can carry at once.</SheetDescription>
      </SheetHeader>

      <form id="rider-form" class="flex-1 space-y-5 overflow-y-auto p-4" novalidate @submit.prevent="save">
        <div class="space-y-2">
          <Label for="rider-name">Full name</Label>
          <Input id="rider-name" v-model="form.name" maxlength="60" autocomplete="off" placeholder="e.g. Ama Mensah" :aria-invalid="!!form.errors.name" />
          <FieldError :message="form.errors.name" />
        </div>
        <div class="space-y-2">
          <Label for="rider-phone">Phone number</Label>
          <Input id="rider-phone" v-model="form.phone" type="tel" inputmode="tel" maxlength="20" placeholder="+233 24 000 0000" :aria-invalid="!!form.errors.phone" />
          <FieldError :message="form.errors.phone" />
        </div>
        <div class="grid gap-5 sm:grid-cols-2">
          <div class="space-y-2">
            <Label for="rider-service">Delivery service</Label>
            <Select v-model="form.service">
              <SelectTrigger id="rider-service" class="w-full"><SelectValue /></SelectTrigger>
              <SelectContent>
                <SelectItem v-for="s in SERVICES" :key="s.value" :value="s.value">{{ s.label }}</SelectItem>
              </SelectContent>
            </Select>
          </div>
          <div class="space-y-2">
            <Label for="rider-capacity">Runs at once</Label>
            <Input id="rider-capacity" v-model.number="form.capacity" type="number" inputmode="numeric" min="1" max="30" :aria-invalid="!!form.errors.capacity" />
            <FieldError :message="form.errors.capacity" />
          </div>
        </div>
        <div class="space-y-2">
          <Label for="rider-vehicle">Vehicle / registration <span class="text-muted-foreground font-normal">(optional)</span></Label>
          <Input id="rider-vehicle" v-model="form.vehicle" maxlength="60" placeholder="Motorbike · GR 1234-24" />
        </div>
        <label for="rider-active" class="hover:bg-muted/50 flex cursor-pointer items-start justify-between gap-4 rounded-xl border p-4 transition-colors">
          <span class="space-y-0.5">
            <span class="block text-sm font-medium">Available for assignments</span>
            <span class="text-muted-foreground block text-xs">Inactive riders keep their history but leave the picker.</span>
          </span>
          <Switch id="rider-active" v-model="form.isActive" />
        </label>
      </form>

      <SheetFooter class="flex-row justify-end border-t">
        <Button type="button" variant="ghost" @click="open = false">Cancel</Button>
        <Button type="submit" form="rider-form" :disabled="form.processing || !form.isDirty && !!rider">
          <Loader2 v-if="form.processing" class="animate-spin" aria-hidden="true" />
          {{ rider ? 'Save changes' : 'Add rider' }}
        </Button>
      </SheetFooter>
    </SheetContent>
  </Sheet>
</template>
