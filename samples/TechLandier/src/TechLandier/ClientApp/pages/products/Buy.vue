<script setup lang="ts">
import { computed, nextTick, ref, useTemplateRef, watch } from 'vue'
import { Deferred, Head, Link, useForm } from '@inertiajs/vue3'
import { ArrowLeft, Check, CreditCard, Loader2, Lock, Smartphone, Wallet } from '@lucide/vue'
import OptionCard from '@/components/checkout/OptionCard.vue'
import OrderSummary from '@/components/checkout/OrderSummary.vue'
import StepIndicator from '@/components/checkout/StepIndicator.vue'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { Skeleton } from '@/components/ui/skeleton'
import { Switch } from '@/components/ui/switch'
import { Textarea } from '@/components/ui/textarea'
import { PLAN_COUNTS, PLAN_FEE, PLAN_INTERVAL_DAYS, dueDateOf } from '@/lib/installments'
import { money } from '@/lib/money'
import { cn } from '@/lib/utils'
import { fmtDate } from '@/lib/time'
import type { CreditState, ProductDetail as ProductDetailData, ZoneRow } from '@/types'

import StoreHours from '@/components/StoreHours.vue'
import type { StoreSettings } from '@/types/management'
const props = defineProps<{
  settings: StoreSettings
  product: ProductDetailData
  qty: number
  zones: ZoneRow[]
  insure: boolean
  bnpl: string | null
  creditState?: CreditState | null
}>()

const steps = ['Delivery', 'Payment', 'Review']
const step = ref(0)

const form = useForm({
  productId: props.product.id,
  qty: props.qty,
  zoneId: null as string | null,
  paymentMethod: 'card',
  recipientName: '',
  phone: '',
  streetLabel: '',
  notes: '',
  insurance: props.insure && props.product.isInsurable,
  bnplCount: (props.bnpl === '3' || props.bnpl === '6' || props.bnpl === '12' ? Number(props.bnpl) : null) as number | null,
  // One stable idempotency key per visit: a retry resubmits the same attemptId and the
  // server returns the same order instead of double-charging.
  checkoutAttemptId: crypto.randomUUID(),
})

const zone = computed(() => props.zones.find((z) => z.id === form.zoneId) ?? null)
const creditReady = computed(() => props.creditState !== undefined)
const bnplBlocked = computed(() => !creditReady.value || props.creditState?.blocked === true || (props.creditState?.planCount ?? 0) >= 2)
const financing = computed(() => !!form.bnplCount)

// Installments and cash on delivery are mutually exclusive: one payment plan per order.
const podDisabled = computed(() => !zone.value?.allowPayOnDelivery || financing.value)
const methods = computed(() => [
  { value: 'card', label: 'Card', icon: CreditCard, hint: 'Confirmed instantly', off: false },
  { value: 'mobile_money', label: 'Mobile money', icon: Smartphone, hint: 'Approve the prompt on your phone', off: false },
  {
    value: 'pay_on_delivery', label: 'Pay on delivery', icon: Wallet, off: podDisabled.value,
    hint: financing.value ? 'Not available with installments' : !zone.value ? 'Pick a delivery zone first' : zone.value.allowPayOnDelivery ? 'Pay the rider in cash' : 'Not offered in this zone',
  },
])
watch(podDisabled, (off) => {
  if (off && form.paymentMethod === 'pay_on_delivery') form.paymentMethod = 'card'
})
watch(() => form.paymentMethod, (m) => {
  if (m === 'pay_on_delivery') form.bnplCount = null
})
// A plan preselected by the URL must not survive a blocked/at-limit credit state, and the customer is told why it went.
const droppedPlan = ref<number | null>(null)
const blockedReason = computed(() =>
  props.creditState?.blocked ? 'you have an overdue installment' : (props.creditState?.planCount ?? 0) >= 2 ? 'you already have two active plans' : '')
watch(() => props.creditState, (state) => {
  if (state !== undefined && (state?.blocked === true || (state?.planCount ?? 0) >= 2)) {
    if (form.bnplCount) droppedPlan.value = form.bnplCount
    form.bnplCount = null
  }
})

// Totals mirror the server: goods at display price + zone fee + 15% premium on the price paid, then the 5% plan fee.
const round2 = (n: number) => Math.round(n * 100) / 100
const premiumPrice = computed(() => round2(props.product.displayPrice * 0.15))
const payout = computed(() => round2(props.product.displayPrice * 0.9))
const goodsTotal = computed(() => props.product.displayPrice * form.qty)
const premium = computed(() => (form.insurance && props.product.isInsurable ? premiumPrice.value : 0))
const shipping = computed(() => zone.value?.fee ?? 0)
const total = computed(() => goodsTotal.value + shipping.value + premium.value)
const planTotal = computed(() => round2(total.value * (1 + PLAN_FEE)))
const planFee = computed(() => round2(planTotal.value - total.value))
const perInstallment = computed(() => (form.bnplCount ? round2(planTotal.value / form.bnplCount) : 0))
const grandTotal = computed(() => (form.bnplCount ? planTotal.value : total.value))
// Due dates count 14 days per installment from the order day, like the server.
const schedule = computed(() => (form.bnplCount ? Array.from({ length: form.bnplCount }, (_, i) => ({ number: i + 1, due: dueDateOf(i + 1), amount: perInstallment.value })) : []))

// ---- step gating: required fields per step, plus jump to the step a server error belongs to
const missing = computed(() => ({
  zoneId: !form.zoneId,
  recipientName: !form.recipientName.trim(),
  phone: !form.phone.trim(),
  streetLabel: !form.streetLabel.trim(),
}))
const touched = ref(false)
const stepOneOk = computed(() => !Object.values(missing.value).some(Boolean))
const formEl = useTemplateRef<HTMLFormElement>('formEl')

/** Moves focus to the first invalid field of step one and brings it into view. */
async function focusFirstInvalid() {
  await nextTick()
  const first = (Object.keys(missing.value) as (keyof typeof missing.value)[]).find((k) => missing.value[k])
  if (!first) return
  const el = formEl.value?.querySelector<HTMLElement>(first === 'zoneId' ? 'input[name=zoneId]' : `#${first}`)
  el?.focus({ preventScroll: true })
  const reduce = window.matchMedia('(prefers-reduced-motion: reduce)').matches
  ;(first === 'zoneId' ? el?.closest('fieldset') : el)?.scrollIntoView({ block: 'center', behavior: reduce ? 'auto' : 'smooth' })
}

const next = () => {
  if (step.value === 0 && !stepOneOk.value) {
    touched.value = true
    void focusFirstInvalid()
    return
  }
  step.value = Math.min(steps.length - 1, step.value + 1)
  window.scrollTo({ top: 0 })
}
const goTo = (i: number) => {
  step.value = i
  window.scrollTo({ top: 0 })
}
const stepOf: Record<string, number> = { zoneId: 0, recipientName: 0, phone: 0, streetLabel: 0, notes: 0, paymentMethod: 1, bnplCount: 1, insurance: 1 }
watch(() => form.errors, (errs) => {
  const first = Object.keys(errs).map((k) => stepOf[k] ?? 2).sort()[0]
  if (first !== undefined) step.value = first
})

const err = (k: keyof typeof missing.value) => form.errors[k] || (touched.value && missing.value[k] ? 'This field is required.' : '')
const submit = () => {
  if (step.value < steps.length - 1) return next()
  form.post('/checkout')
}
</script>

<template>
  <Head :title="`Checkout: ${product.name}`" />

  <div class="mx-auto max-w-6xl px-4 py-8 pb-32 sm:px-6 lg:px-8 lg:pb-8">
    <div class="flex flex-wrap items-center justify-between gap-4">
      <h1 class="text-2xl font-semibold tracking-tight">Checkout</h1>
      <StepIndicator :steps="steps" :current="step" @go="goTo" />
    </div>

    <form ref="formEl" class="mt-8 grid gap-8 lg:grid-cols-[minmax(0,1fr)_23rem]" novalidate @submit.prevent="submit">
      <div class="min-w-0 space-y-8">
        <button v-if="step > 0" type="button" class="text-primary -mb-4 inline-flex min-h-11 items-center gap-1.5 text-sm font-medium hover:underline" @click="goTo(step - 1)">
          <ArrowLeft class="size-4" aria-hidden="true" /> Back to {{ steps[step - 1]!.toLowerCase() }}
        </button>

        <!-- 1. Delivery -->
        <template v-if="step === 0">
          <fieldset class="surface scroll-mt-24 space-y-4 p-5 sm:p-6">
            <legend class="sr-only">Delivery zone</legend>
            <h2 class="text-lg font-semibold">Where should we deliver?</h2>
            <!-- Compact radio rows: eight zones stay scannable on a phone. -->
            <div class="divide-y overflow-hidden rounded-lg border">
              <label
                v-for="z in zones" :key="z.id"
                :class="cn('has-[:focus-visible]:ring-ring flex min-h-14 cursor-pointer items-center gap-3 px-4 py-2 text-sm transition-colors has-[:focus-visible]:ring-2 has-[:focus-visible]:ring-inset', form.zoneId === z.id ? 'bg-accent/60' : 'hover:bg-muted/60')"
              >
                <input v-model="form.zoneId" type="radio" name="zoneId" :value="z.id" class="sr-only" />
                <span :class="cn('flex size-5 shrink-0 items-center justify-center rounded-full border', form.zoneId === z.id ? 'border-primary bg-primary text-primary-foreground' : 'border-input')" aria-hidden="true">
                  <Check v-if="form.zoneId === z.id" class="size-3" :stroke-width="3" />
                </span>
                <span class="min-w-0 flex-1">
                  <span class="block font-medium">{{ z.name }}</span>
                  <span class="text-muted-foreground block text-xs">{{ z.etaText }}<template v-if="z.allowPayOnDelivery"> · pay on delivery</template></span>
                </span>
                <span class="price shrink-0 font-medium">{{ z.fee === 0 ? 'Free' : money(z.fee) }}</span>
              </label>
            </div>
            <p v-if="!zones.length" class="text-muted-foreground text-sm">No delivery zones are active right now. Check back soon.</p>
            <p v-if="err('zoneId')" role="alert" class="text-destructive text-sm">{{ form.errors.zoneId || 'Choose a delivery zone.' }}</p>
          </fieldset>

          <section class="surface space-y-4 p-5 sm:p-6" aria-labelledby="addr-h">
            <h2 id="addr-h" class="text-lg font-semibold">Who is receiving it?</h2>
            <div class="grid gap-4 sm:grid-cols-2">
              <div class="space-y-1.5">
                <Label for="recipientName">Full name</Label>
                <Input id="recipientName" v-model="form.recipientName" class="scroll-mt-28" autocomplete="name" required :aria-invalid="!!err('recipientName')" />
                <p v-if="err('recipientName')" role="alert" class="text-destructive text-sm">{{ err('recipientName') }}</p>
              </div>
              <div class="space-y-1.5">
                <Label for="phone">Phone number</Label>
                <Input id="phone" v-model="form.phone" class="scroll-mt-28" type="tel" inputmode="tel" autocomplete="tel" maxlength="24" required :aria-invalid="!!err('phone')" />
                <p v-if="err('phone')" role="alert" class="text-destructive text-sm">{{ err('phone') }}</p>
              </div>
              <div class="space-y-1.5 sm:col-span-2">
                <Label for="streetLabel">Digital address (GhanaPost GPS)</Label>
                <Input id="streetLabel" v-model="form.streetLabel" class="scroll-mt-28" placeholder="GA-183-5287 Nyaneba Avenue" required :aria-invalid="!!err('streetLabel')" />
                <p v-if="err('streetLabel')" role="alert" class="text-destructive text-sm">{{ err('streetLabel') }}</p>
              </div>
              <div class="space-y-1.5 sm:col-span-2">
                <Label for="notes">Notes for the rider <span class="text-muted-foreground font-normal">(optional)</span></Label>
                <Textarea id="notes" v-model="form.notes" rows="2" maxlength="400" />
              </div>
            </div>
          </section>
        </template>

        <!-- 2. Payment -->
        <template v-else-if="step === 1">
          <fieldset class="surface space-y-4 p-5 sm:p-6">
            <legend class="sr-only">Payment method</legend>
            <h2 class="text-lg font-semibold">How will you pay?</h2>
            <div class="grid gap-3 sm:grid-cols-3">
              <OptionCard v-for="m in methods" :key="m.value" :selected="form.paymentMethod === m.value" :disabled="m.off">
                <input v-model="form.paymentMethod" type="radio" name="paymentMethod" :value="m.value" :disabled="m.off" class="sr-only" />
                <span class="flex items-center gap-2 font-medium"><component :is="m.icon" class="size-4" aria-hidden="true" /> {{ m.label }}</span>
                <span class="text-muted-foreground mt-0.5 block text-xs">{{ m.hint }}</span>
              </OptionCard>
            </div>
            <p v-if="form.errors.paymentMethod" role="alert" class="text-destructive text-sm">{{ form.errors.paymentMethod }}</p>
          </fieldset>

          <fieldset class="surface space-y-4 p-5 sm:p-6">
            <legend class="sr-only">Installments</legend>
            <div>
              <h2 class="text-lg font-semibold">Pay in full or in installments</h2>
              <p class="text-muted-foreground mt-1 text-sm">
                Installments add a flat 5% plan fee ({{ money(planFee) }} on this order) and are due every {{ PLAN_INTERVAL_DAYS }} days, with no down payment.
              </p>
            </div>
            <div class="grid gap-3 sm:grid-cols-4">
              <OptionCard :selected="form.bnplCount === null">
                <input v-model="form.bnplCount" type="radio" name="bnplCount" :value="null" class="sr-only" />
                <span class="block font-medium">Pay in full</span>
                <span class="price text-muted-foreground block text-xs">{{ money(total) }} today</span>
              </OptionCard>
              <Deferred data="creditState">
                <template #fallback>
                  <Skeleton v-for="i in 3" :key="i" class="h-20 rounded-xl" />
                </template>
                <OptionCard v-for="n in PLAN_COUNTS" :key="n" :selected="form.bnplCount === n" :disabled="bnplBlocked || form.paymentMethod === 'pay_on_delivery'">
                  <input v-model="form.bnplCount" type="radio" name="bnplCount" :value="n" :disabled="bnplBlocked || form.paymentMethod === 'pay_on_delivery'" class="sr-only" />
                  <span class="block font-medium">{{ n }} payments</span>
                  <span class="price text-muted-foreground block text-xs">{{ money(planTotal / n) }} each</span>
                  <span class="price text-muted-foreground block text-xs">{{ money(planTotal) }} total</span>
                </OptionCard>
              </Deferred>
            </div>
            <p v-if="creditReady && bnplBlocked" class="bg-accent text-accent-foreground rounded-lg px-3 py-2.5 text-sm leading-5" role="status">
              <template v-if="droppedPlan">We removed the {{ droppedPlan }} installments you picked because {{ blockedReason }}. </template>
              <template v-else>Installments are paused{{ blockedReason ? ` because ${blockedReason}` : '' }}. </template>
              You can still pay in full, or manage your plans in <Link href="/account/credit" class="underline underline-offset-2">your installment plans</Link>.
            </p>
            <p v-if="form.errors.bnplCount" role="alert" class="text-destructive text-sm">{{ form.errors.bnplCount }}</p>
          </fieldset>

          <section v-if="product.isInsurable" class="surface flex items-start gap-4 p-5 sm:p-6">
            <div class="min-w-0 flex-1">
              <h2 id="ins-h" class="font-semibold">Add device insurance <span class="price text-muted-foreground font-normal">+{{ money(premiumPrice) }}</span></h2>
              <p class="text-muted-foreground mt-1 text-sm leading-5">12 months against damage, theft or malfunction. An approved claim pays out {{ money(payout) }}.</p>
            </div>
            <Switch v-model="form.insurance" aria-labelledby="ins-h" />
          </section>
        </template>

        <!-- 3. Review -->
        <section v-else class="surface space-y-6 p-5 sm:p-6" aria-labelledby="review-h">
          <h2 id="review-h" class="text-lg font-semibold">Check and place your order</h2>
          <dl class="divide-y text-sm">
            <div class="grid gap-1 py-3 sm:grid-cols-[10rem_1fr]">
              <dt class="text-muted-foreground">Deliver to</dt>
              <dd>{{ form.recipientName }}, {{ form.phone }}<br />{{ form.streetLabel }}<br /><span class="text-muted-foreground">{{ zone?.name }} · {{ zone?.etaText }}</span></dd>
            </div>
            <div class="grid gap-1 py-3 sm:grid-cols-[10rem_1fr]">
              <dt class="text-muted-foreground">Payment</dt>
              <dd>
                {{ methods.find((m) => m.value === form.paymentMethod)?.label }}
                <span class="text-muted-foreground">· {{ form.bnplCount ? `${form.bnplCount} installments of ${money(perInstallment)}` : 'paid in full' }}</span>
              </dd>
            </div>
            <div v-if="form.bnplCount" class="grid gap-1 py-3 sm:grid-cols-[10rem_1fr]">
              <dt class="text-muted-foreground">Plan total</dt>
              <dd>
                <span class="price font-semibold">{{ money(planTotal) }}</span>
                <span class="text-muted-foreground"> including a {{ money(planFee) }} plan fee</span>
                <span class="mt-1 block">First payment {{ money(perInstallment) }} on {{ fmtDate(schedule[0]!.due, true) }}, then every {{ PLAN_INTERVAL_DAYS }} days until {{ fmtDate(schedule[schedule.length - 1]!.due, true) }}.</span>
              </dd>
            </div>
            <div v-if="form.insurance && product.isInsurable" class="grid gap-1 py-3 sm:grid-cols-[10rem_1fr]">
              <dt class="text-muted-foreground">Insurance</dt><dd>12 months, {{ money(premium) }}</dd>
            </div>
            <div class="grid gap-1 py-3 sm:grid-cols-[10rem_1fr]">
              <dt class="text-muted-foreground">{{ form.bnplCount ? 'Order total' : 'Total today' }}</dt>
              <dd class="price font-semibold">{{ money(total) }}<span v-if="form.bnplCount" class="text-muted-foreground font-normal"> before the plan fee</span></dd>
            </div>
          </dl>
          <div class="flex flex-wrap gap-2">
            <Button type="button" variant="outline" size="sm" @click="goTo(0)">Edit delivery</Button>
            <Button type="button" variant="outline" size="sm" @click="goTo(1)">Edit payment</Button>
          </div>
        </section>

        <!-- Phones: the running total and the primary action stay pinned above the home bar on every step. -->
        <div class="flex items-center justify-between gap-3 max-lg:bg-background/95 max-lg:fixed max-lg:inset-x-0 max-lg:bottom-0 max-lg:z-30 max-lg:border-t max-lg:px-4 max-lg:pt-3 max-lg:pb-[calc(0.75rem+env(safe-area-inset-bottom))] max-lg:backdrop-blur lg:justify-end">
          <div class="min-w-0 lg:hidden" aria-live="polite">
            <p class="text-muted-foreground truncate text-xs">
              {{ form.bnplCount ? `Plan total · ${form.bnplCount} × ${money(perInstallment)}` : zone ? 'Total' : 'Total before delivery' }}
            </p>
            <p class="price text-base font-semibold">{{ money(grandTotal) }}</p>
          </div>
          <Button v-if="step < steps.length - 1" type="submit" size="lg" class="shrink-0 sm:min-w-40">Continue</Button>
          <Button v-else type="submit" size="lg" class="shrink-0 sm:min-w-48" :disabled="form.processing || !zones.length">
            <Loader2 v-if="form.processing" class="animate-spin" aria-hidden="true" />
            <Lock v-else aria-hidden="true" />
            {{ form.processing ? 'Placing order…' : 'Place order' }}
          </Button>
        </div>
      </div>

      <aside class="lg:sticky lg:top-24 lg:self-start">
        <OrderSummary
          v-model:qty="form.qty" :product="product" :shipping="zone ? shipping : null" :premium="premium" :total="total"
          :readonly="step === steps.length - 1" :installments="form.bnplCount" :per-installment="perInstallment" :plan-fee="planFee" :schedule="schedule"
        />
      </aside>
    </form>

    <StoreHours :settings="settings" class="mt-10" />
  </div>
</template>
