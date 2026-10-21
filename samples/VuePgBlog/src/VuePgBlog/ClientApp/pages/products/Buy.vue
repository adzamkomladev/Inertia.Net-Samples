<script setup lang="ts">
import { Head, useForm } from '@inertiajs/vue3'
import { computed, ref } from 'vue'
import { CreditCard, Loader2, Lock, ShieldCheck, Smartphone, Truck, Wallet } from '@lucide/vue'
import ProductArt from '@/components/ProductArt.vue'
import PriceTag from '@/components/PriceTag.vue'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select'
import { Switch } from '@/components/ui/switch'
import { Textarea } from '@/components/ui/textarea'
import { money } from '@/lib/money'
import type { CreditState, ProductDetail as ProductDetailData, ZoneRow } from '@/types'

const props = defineProps<{
  product: ProductDetailData
  qty: number
  zones: ZoneRow[]
  insure: boolean
  bnpl: string | null
  creditState: CreditState | null
}>()

// One stable idempotency key per visit: a retry resubmits the same attemptId and the
// server returns the same order instead of double-charging.
const attemptId = ref(crypto.randomUUID())

const zoneId = ref<string | null>(null)
const zone = computed(() => props.zones.find((z) => z.name === zoneId.value) ?? null)

const methods = computed(() => {
  const all = [
    { value: 'card', label: 'Card', icon: CreditCard, hint: 'Instant confirmation' },
    { value: 'mobile_money', label: 'Mobile money', icon: Smartphone, hint: 'Push to your wallet' },
    { value: 'pay_on_delivery', label: 'Pay on delivery', icon: Wallet, hint: 'Cash to the rider', disabled: !zone.value?.allowPayOnDelivery },
  ]
  return all
})

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
})

const bnplBlocked = computed(() => props.creditState?.blocked === true || (props.creditState?.planCount ?? 0) >= 2)
const bnplOptions = [3, 6, 12]

// Totals mirror the server: goods at display price + zone fee + 15% premium on list price.
const goodsTotal = computed(() => props.product.displayPrice * form.qty)
const premium = computed(() => (form.insurance && props.product.isInsurable ? Math.round(props.product.price * 0.15 * 100) / 100 : 0))
const shipping = computed(() => zone.value?.fee ?? 0)
const total = computed(() => goodsTotal.value + shipping.value + premium.value)
const bnplTotal = computed(() => Math.round(total.value * 1.05 * 100) / 100)
const perInstallment = computed(() => (form.bnplCount ? Math.round((bnplTotal.value / form.bnplCount) * 100) / 100 : 0))

const submit = () =>
  form.post('/checkout', { data: { ...form.data(), checkoutAttemptId: attemptId.value } as never })
</script>

<template>
  <Head :title="`Buy ${product.name}`" />

  <div class="mx-auto max-w-6xl px-4 py-8 sm:px-6 lg:px-8">
    <h1 class="font-display text-2xl font-bold tracking-tight">Checkout</h1>
    <p class="text-muted-foreground mt-1 text-sm">Confirm the essentials — delivery, payment and how you'd like to split it.</p>

    <form class="mt-8 grid gap-8 lg:grid-cols-[1fr_24rem]" novalidate @submit.prevent="submit">
      <div class="min-w-0 space-y-6">
        <!-- Quantity -->
        <section class="bg-card rounded-3xl border p-6 shadow-xs">
          <h2 class="font-display text-base font-semibold">Quantity</h2>
          <div class="mt-3 flex items-center gap-3">
            <Button type="button" variant="outline" size="icon" class="rounded-xl" aria-label="Decrease quantity" @click="form.qty = Math.max(1, form.qty - 1)">−</Button>
            <span class="price w-10 text-center text-lg font-semibold">{{ form.qty }}</span>
            <Button type="button" variant="outline" size="icon" class="rounded-xl" aria-label="Increase quantity" @click="form.qty = Math.min(10, form.qty + 1)">+</Button>
            <p class="text-muted-foreground ml-2 text-xs">{{ product.stock }} in stock · max 10 per order</p>
          </div>
        </section>

        <!-- Delivery zone -->
        <section class="bg-card rounded-3xl border p-6 shadow-xs">
          <h2 class="font-display flex items-center gap-2 text-base font-semibold"><Truck class="text-brand size-4" aria-hidden="true" /> Delivery zone</h2>
          <div class="mt-3 grid gap-2 sm:grid-cols-2">
            <label
              v-for="z in zones" :key="z.name"
              class="flex cursor-pointer items-start gap-3 rounded-2xl border p-3.5 transition-colors has-[:focus-visible]:ring-2 has-[:focus-visible]:ring-ring/50"
              :class="form.zoneId === z.name ? 'border-brand/50 bg-brand/5' : 'hover:bg-muted/40'"
            >
              <input v-model="form.zoneId" type="radio" name="zoneId" :value="z.name" class="accent-brand mt-0.5" />
              <span class="min-w-0 text-sm">
                <span class="block font-medium">{{ z.name }} — <span class="price">{{ z.fee === 0 ? 'Free' : money(z.fee) }}</span></span>
                <span class="text-muted-foreground block text-xs">{{ z.etaText }}</span>
                <span v-if="z.allowPayOnDelivery" class="text-success mt-0.5 block text-xs font-medium">Pay on delivery available</span>
              </span>
            </label>
            <p v-if="!zones.length" class="text-muted-foreground text-sm">No active delivery zones right now.</p>
          </div>
          <p v-if="form.errors.zoneId" role="alert" class="text-destructive mt-2 text-sm">{{ form.errors.zoneId }}</p>
        </section>

        <!-- Payment -->
        <section class="bg-card rounded-3xl border p-6 shadow-xs">
          <h2 class="font-display text-base font-semibold">Payment method</h2>
          <div class="mt-3 grid gap-2 sm:grid-cols-3">
            <label
              v-for="m in methods" :key="m.value"
              class="flex cursor-pointer flex-col gap-1 rounded-2xl border p-3.5 transition-colors"
              :class="[form.paymentMethod === m.value ? 'border-brand/50 bg-brand/5' : 'hover:bg-muted/40', m.disabled ? 'cursor-not-allowed opacity-45' : '']"
            >
              <span class="flex items-center gap-2 text-sm font-medium">
                <component :is="m.icon" class="text-brand size-4" aria-hidden="true" />
                {{ m.label }}
                <input v-model="form.paymentMethod" type="radio" name="paymentMethod" :value="m.value" :disabled="m.disabled" class="accent-brand ml-auto" />
              </span>
              <span class="text-muted-foreground text-xs">{{ m.disabled ? 'Unavailable in this zone' : m.hint }}</span>
            </label>
          </div>
        </section>

        <!-- BNPL -->
        <section class="bg-card rounded-3xl border p-6 shadow-xs">
          <h2 class="font-display flex items-center gap-2 text-base font-semibold"><CreditCard class="text-brand size-4" aria-hidden="true" /> Split into installments <span class="text-muted-foreground text-xs font-normal">(optional)</span></h2>
          <div class="mt-3 grid gap-2 sm:grid-cols-4">
            <label
              class="flex cursor-pointer flex-col items-center gap-0.5 rounded-2xl border p-3 transition-colors"
              :class="form.bnplCount === null ? 'border-brand/50 bg-brand/5' : 'hover:bg-muted/40'"
            >
              <input v-model="form.bnplCount" type="radio" name="bnplCount" :value="null" class="accent-brand sr-only" />
              <span class="text-sm font-medium">Pay in full</span>
              <span class="price text-muted-foreground text-xs">{{ money(total) }}</span>
            </label>
            <label
              v-for="n in bnplOptions" :key="n"
              class="relative flex cursor-pointer flex-col items-center gap-0.5 rounded-2xl border p-3 transition-colors"
              :class="[form.bnplCount === n ? 'border-brand/50 bg-brand/5' : 'hover:bg-muted/40', bnplBlocked ? 'cursor-not-allowed opacity-45' : '']"
            >
              <input v-model="form.bnplCount" type="radio" name="bnplCount" :value="n" :disabled="bnplBlocked" class="sr-only" />
              <span class="text-sm font-medium">{{ n }} installments</span>
              <span class="price text-muted-foreground text-xs">{{ money(bnplTotal / n) }}/mo-equiv</span>
            </label>
          </div>
          <p v-if="bnplBlocked" class="text-warning mt-3 rounded-xl bg-warning/10 px-3 py-2 text-xs leading-5">
            BNPL is paused: settle overdue installments or finish an active plan first (max 2).
            Manage plans in <a href="/account/credit" class="text-brand underline underline-offset-2">Credit &amp; BNPL</a>.
          </p>
          <p v-else-if="form.bnplCount" class="text-muted-foreground mt-3 text-xs leading-5">
            Plan total {{ money(bnplTotal) }} (includes the 5% BNPL fee), {{ form.bnplCount }} installments every 14 days,
            first one {{ money(perInstallment) }}.
          </p>
        </section>

        <!-- Insurance -->
        <section v-if="product.isInsurable" class="bg-card rounded-3xl border p-6 shadow-xs">
          <div class="flex items-center justify-between gap-4">
            <div class="flex items-start gap-3">
              <ShieldCheck class="text-brand mt-0.5 size-5" aria-hidden="true" />
              <div>
                <p class="text-sm font-semibold">Add device insurance</p>
                <p class="text-muted-foreground text-xs leading-5">
                  12 months of cover for damage, theft or malfunction — payout {{ money(product.price * 0.9) }}.
                </p>
              </div>
            </div>
            <div class="flex items-center gap-3">
              <span class="price text-sm font-semibold">{{ money(product.price * 0.15) }}</span>
              <Switch v-model="form.insurance" aria-label="Add device insurance" />
            </div>
          </div>
        </section>

        <!-- Address -->
        <section class="bg-card rounded-3xl border p-6 shadow-xs">
          <h2 class="font-display text-base font-semibold">Delivery details</h2>
          <div class="mt-3 grid gap-4 sm:grid-cols-2">
            <div class="space-y-2">
              <Label for="recipientName">Recipient name</Label>
              <Input id="recipientName" v-model="form.recipientName" autocomplete="name" required :aria-invalid="!!form.errors.recipientName" />
              <p v-if="form.errors.recipientName" role="alert" class="text-destructive text-sm">{{ form.errors.recipientName }}</p>
            </div>
            <div class="space-y-2">
              <Label for="phone">Phone</Label>
              <Input id="phone" v-model="form.phone" type="tel" autocomplete="tel" maxlength="24" required :aria-invalid="!!form.errors.phone" />
              <p v-if="form.errors.phone" role="alert" class="text-destructive text-sm">{{ form.errors.phone }}</p>
            </div>
            <div class="space-y-2 sm:col-span-2">
              <Label for="streetLabel">Digital address (GhanaPost GPS)</Label>
              <Input id="streetLabel" v-model="form.streetLabel" placeholder="GA-183-5287 Nyaneba Avenue" required :aria-invalid="!!form.errors.streetLabel" />
              <p v-if="form.errors.streetLabel" role="alert" class="text-destructive text-sm">{{ form.errors.streetLabel }}</p>
            </div>
            <div class="space-y-2 sm:col-span-2">
              <Label for="notes">Notes for the rider <span class="text-muted-foreground font-normal">(optional)</span></Label>
              <Textarea id="notes" v-model="form.notes" rows="2" maxlength="400" />
            </div>
          </div>
        </section>
      </div>

      <!-- Summary -->
      <aside class="lg:sticky lg:top-24 lg:self-start">
        <div class="bg-card space-y-4 rounded-3xl border p-6 shadow-xs">
          <div class="flex gap-4">
            <div class="w-20 shrink-0 overflow-hidden rounded-xl">
              <ProductArt :seed="product.slug" :icon="product.icon" :brand="product.brand" :image-url="product.imageUrl" :alt="product.name" />
            </div>
            <div class="min-w-0">
              <p class="text-brand text-xs font-semibold tracking-wide uppercase">{{ product.brand }}</p>
              <p class="line-clamp-2 text-sm font-medium">{{ product.name }}</p>
              <PriceTag :price="product.displayPrice" :compare-at="product.compareAtPrice" :discount-percent="product.discountPercent" size="sm" />
            </div>
          </div>

          <dl class="divide-y text-sm">
            <div class="flex justify-between py-2"><dt class="text-muted-foreground">Goods ({{ form.qty }} × {{ money(product.displayPrice) }})</dt><dd class="price">{{ money(goodsTotal) }}</dd></div>
            <div class="flex justify-between py-2"><dt class="text-muted-foreground">Delivery</dt><dd class="price">{{ zone ? money(shipping) : '—' }}</dd></div>
            <div v-if="premium > 0" class="flex justify-between py-2"><dt class="text-muted-foreground">Insurance (12 mo)</dt><dd class="price">{{ money(premium) }}</dd></div>
            <div class="flex justify-between py-2 text-base font-semibold"><dt>Total</dt><dd class="price">{{ money(total) }}</dd></div>
            <div v-if="form.bnplCount" class="text-success flex justify-between py-1 text-xs"><dt>per installment × {{ form.bnplCount }}</dt><dd class="price">{{ money(perInstallment) }}</dd></div>
          </dl>

          <Button type="submit" size="lg" class="glow w-full rounded-2xl font-semibold" :disabled="form.processing || !zones.length">
            <Loader2 v-if="form.processing" class="animate-spin" aria-hidden="true" />
            <Lock v-else class="size-4" aria-hidden="true" />
            {{ form.processing ? 'Placing order…' : 'Place order' }}
          </Button>
          <p class="text-muted-foreground text-center text-xs leading-5">
            Idempotent checkout: double-clicks can't double-charge.
          </p>
        </div>
      </aside>
    </form>
  </div>
</template>
