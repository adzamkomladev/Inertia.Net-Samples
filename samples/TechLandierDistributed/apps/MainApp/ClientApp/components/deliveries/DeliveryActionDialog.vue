<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import { useForm } from '@inertiajs/vue3'
import { Loader2 } from '@lucide/vue'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { Textarea } from '@/components/ui/textarea'
import { RadioGroup, RadioGroupItem } from '@/components/ui/radio-group'
import { Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle } from '@/components/ui/dialog'
import FieldError from '@/components/FieldError.vue'
import { money } from '@/lib/money'
import type { DeliveryRow } from '@/types'

export type DeliveryAction = 'deliver' | 'fail' | 'note'

const props = defineProps<{ delivery: DeliveryRow | null; action: DeliveryAction }>()
const open = defineModel<boolean>('open', { required: true })

const FAIL_REASONS = [
  'Customer not reachable by phone',
  'Nobody at the address',
  'Address could not be found',
  'Customer refused the parcel',
  'Customer asked to reschedule',
]
const OTHER = '__other'

const reasonChoice = ref(FAIL_REASONS[0]!)
const form = useForm({ cash: 0, receivedBy: '', reason: '', note: '' })

const cashOnDelivery = computed(() => props.delivery?.paymentMethod === 'pay_on_delivery')
const shortfall = computed(() => cashOnDelivery.value && props.delivery != null && form.cash < props.delivery.total)

watch(open, (isOpen) => {
  if (!isOpen || !props.delivery) return
  form.clearErrors()
  form.defaults({ cash: props.delivery.total, receivedBy: props.delivery.customer, reason: '', note: props.delivery.note ?? '' })
  form.reset()
  reasonChoice.value = FAIL_REASONS[0]!
})

const copy = computed(() => ({
  deliver: { title: 'Confirm handover', submit: 'Mark delivered', description: 'Closes the order and notifies the customer.' },
  fail: { title: 'Log a failed attempt', submit: 'Mark failed', description: 'The run goes back to the queue so you can reassign it.' },
  note: { title: 'Note for the rider', submit: 'Save note', description: 'Shown on the board next to this run.' },
})[props.action])

function submit() {
  const d = props.delivery
  if (!d) return
  const body = {
    deliver: () => ({ cash: cashOnDelivery.value ? form.cash : null, receivedBy: form.receivedBy }),
    fail: () => ({ reason: reasonChoice.value === OTHER ? form.reason : reasonChoice.value }),
    note: () => ({ note: form.note }),
  }[props.action]
  form.transform(body).post(`/admin/deliveries/${d.id}/${props.action}`, {
    preserveScroll: true,
    onSuccess: () => (open.value = false),
  })
}
</script>

<template>
  <Dialog v-model:open="open">
    <DialogContent class="sm:max-w-md">
      <DialogHeader>
        <DialogTitle>{{ copy.title }} <span v-if="delivery" class="text-muted-foreground font-normal">· {{ delivery.orderNumber }}</span></DialogTitle>
        <DialogDescription>{{ copy.description }}</DialogDescription>
      </DialogHeader>

      <form id="delivery-action" class="space-y-4" novalidate @submit.prevent="submit">
        <template v-if="action === 'deliver'">
          <div v-if="cashOnDelivery" class="space-y-2">
            <Label for="cash-collected">Cash collected</Label>
            <div class="relative">
              <span class="text-muted-foreground pointer-events-none absolute inset-y-0 left-3 flex items-center text-sm">$</span>
              <Input id="cash-collected" v-model.number="form.cash" type="number" inputmode="decimal" min="0" step="0.01" class="pl-7" />
            </div>
            <p class="text-xs" :class="shortfall ? 'text-warning' : 'text-muted-foreground'">
              {{ shortfall ? `${money(delivery!.total - form.cash)} short of the order total — it will be recorded as collected.` : `Order total is ${money(delivery?.total)}.` }}
            </p>
          </div>
          <div class="space-y-2">
            <Label for="received-by">Received by</Label>
            <Input id="received-by" v-model="form.receivedBy" maxlength="80" placeholder="Name of the person at the door" :aria-invalid="!!form.errors.receivedBy" />
            <FieldError :message="form.errors.receivedBy" />
          </div>
        </template>

        <template v-else-if="action === 'fail'">
          <RadioGroup v-model="reasonChoice" class="gap-1" aria-label="Why did the attempt fail?">
            <Label
              v-for="r in [...FAIL_REASONS, OTHER]" :key="r" :for="`reason-${r}`"
              class="hover:bg-muted/60 has-[[data-state=checked]]:bg-accent has-[[data-state=checked]]:text-accent-foreground flex min-h-10 cursor-pointer items-center gap-3 rounded-lg px-3 py-2 font-normal transition-colors"
            >
              <RadioGroupItem :id="`reason-${r}`" :value="r" />
              {{ r === OTHER ? 'Something else' : r }}
            </Label>
          </RadioGroup>
          <Transition enter-active-class="transition duration-200" enter-from-class="opacity-0 -translate-y-1">
            <div v-if="reasonChoice === OTHER" class="space-y-2">
              <Label for="fail-reason" class="sr-only">Describe what happened</Label>
              <Textarea id="fail-reason" v-model="form.reason" maxlength="200" rows="3" placeholder="What happened at the door?" autofocus />
            </div>
          </Transition>
          <FieldError :message="form.errors.reason" />
          <p v-if="delivery?.attempts" class="text-muted-foreground text-xs">This will be attempt {{ delivery.attempts + 1 }} logged as failed.</p>
        </template>

        <template v-else>
          <Label for="rider-note" class="sr-only">Note</Label>
          <Textarea id="rider-note" v-model="form.note" maxlength="200" rows="3" placeholder="Call on arrival, gate code, fragile…" />
          <div class="text-muted-foreground flex justify-between text-xs">
            <FieldError :message="form.errors.note" />
            <span class="ml-auto tabular-nums">{{ form.note.length }}/200</span>
          </div>
        </template>
      </form>

      <DialogFooter>
        <Button type="button" variant="ghost" @click="open = false">Cancel</Button>
        <Button type="submit" form="delivery-action" :variant="action === 'fail' ? 'destructive' : 'default'" :disabled="form.processing">
          <Loader2 v-if="form.processing" class="animate-spin" aria-hidden="true" />
          {{ copy.submit }}
        </Button>
      </DialogFooter>
    </DialogContent>
  </Dialog>
</template>
