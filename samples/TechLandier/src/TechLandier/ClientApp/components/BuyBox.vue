<script setup lang="ts">
import { computed, ref, useTemplateRef } from 'vue'
import { useIntersectionObserver } from '@vueuse/core'
import { Link } from '@inertiajs/vue3'
import { Check, ShieldCheck, Truck } from '@lucide/vue'
import InstallmentLine from '@/components/InstallmentLine.vue'
import PriceTag from '@/components/PriceTag.vue'
import RatingStars from '@/components/RatingStars.vue'
import { Button } from '@/components/ui/button'
import { Switch } from '@/components/ui/switch'
import { money } from '@/lib/money'
import { PLAN_COUNTS, PLAN_INTERVAL_DAYS, perInstallment, planFee, planTotal, round2 } from '@/lib/installments'
import { cn } from '@/lib/utils'
import type { ProductDetail } from '@/types'

const props = defineProps<{ product: ProductDetail }>()

// Sticky purchase bar on phones: appears once the in-page buy button has scrolled out of view.
const cta = useTemplateRef<HTMLElement>('cta')
// Only once the button has scrolled out of view above the viewport, so the bar never covers the title on load.
const ctaPassed = ref(false)
useIntersectionObserver(cta, ([entry]) => {
  ctaPassed.value = !!entry && !entry.isIntersecting && entry.boundingClientRect.top < 0
})
const showBar = computed(() => ctaPassed.value && inStock.value)

const inStock = computed(() => props.product.stock > 0)
const lowStock = computed(() => inStock.value && props.product.stock <= props.product.lowStockThreshold)

/** null = pay in full. */
const plan = ref<number | null>(null)
const insure = ref(false)

// Insurance is priced on what the customer pays (the discounted price), same as the server.
const premium = computed(() => round2(props.product.displayPrice * 0.15))
const payout = computed(() => Math.round(props.product.displayPrice * 0.9 * 100) / 100)

// What the plan fee applies to before delivery: the price plus the premium when insured (checkout adds the zone fee on top).
const base = computed(() => round2(props.product.displayPrice + (insure.value && props.product.isInsurable ? premium.value : 0)))

const buyHref = computed(() => {
  const q = new URLSearchParams()
  if (plan.value) q.set('bnpl', String(plan.value))
  if (insure.value && props.product.isInsurable) q.set('insure', 'true')
  const s = q.toString()
  return `/buy/${props.product.id}${s ? `?${s}` : ''}`
})

const options = computed(() => [
  { value: null as number | null, label: 'Pay in full', amount: money(base.value), note: '+ delivery at checkout' },
  ...PLAN_COUNTS.map((n) => ({ value: n as number | null, label: `${n} installments`, amount: money(perInstallment(base.value, n)), note: 'every 2 weeks' })),
])
</script>

<!-- Sticky buy box: price, stock, installment plan picker with the fee explained, insurance, one CTA. -->
<template>
  <div class="surface space-y-5 p-5 sm:p-6">
    <div class="space-y-2">
      <p class="text-muted-foreground text-sm">{{ product.brand }}</p>
      <h1 class="text-2xl font-semibold tracking-tight text-balance">{{ product.name }}</h1>
      <div class="flex flex-wrap items-center gap-x-3 gap-y-1">
        <RatingStars v-if="product.ratingCount > 0" :rating="product.ratingAvg" :count="product.ratingCount" />
        <span v-else class="text-muted-foreground text-xs">No reviews yet</span>
        <span v-if="product.soldCount > 0" class="text-muted-foreground text-xs">{{ product.soldCount.toLocaleString() }} sold</span>
      </div>
    </div>

    <div class="space-y-1.5">
      <PriceTag :price="product.displayPrice" :compare-at="product.compareAtPrice" :discount-percent="product.discountPercent" size="lg" />
      <p v-if="product.discountPercent > 0" class="text-muted-foreground text-sm">You save {{ money(product.price - product.displayPrice) }}</p>
      <p v-if="!inStock" class="text-destructive text-sm font-medium">Out of stock</p>
      <p v-else-if="lowStock" class="text-warning text-sm font-medium">Only {{ product.stock }} left</p>
      <p v-else class="text-success text-sm font-medium">In stock</p>
    </div>

    <fieldset v-if="inStock" class="space-y-2">
      <legend class="mb-2 text-sm font-medium">How would you like to pay?</legend>
      <label
        v-for="o in options" :key="String(o.value)"
        :class="cn('flex min-h-14 cursor-pointer items-center gap-3 rounded-lg border px-4 py-2.5 text-sm transition-colors has-[:focus-visible]:ring-2 has-[:focus-visible]:ring-ring', plan === o.value ? 'border-primary bg-accent/60' : 'hover:bg-muted/60')"
      >
        <input v-model="plan" type="radio" name="plan" :value="o.value" class="sr-only" />
        <span :class="cn('flex size-5 shrink-0 items-center justify-center rounded-full border', plan === o.value ? 'border-primary bg-primary text-primary-foreground' : 'border-input')" aria-hidden="true">
          <Check v-if="plan === o.value" class="size-3" :stroke-width="3" />
        </span>
        <span class="font-medium">{{ o.label }}</span>
        <span class="ml-auto text-right">
          <span class="price block font-semibold">{{ o.amount }}</span>
          <span class="text-muted-foreground block text-xs">{{ o.note }}</span>
        </span>
      </label>
      <p class="text-muted-foreground text-xs leading-5">
        <template v-if="plan">
          Plan total {{ money(planTotal(base)) }}: the price{{ insure ? ' and insurance' : '' }} plus a one-off 5% plan fee ({{ money(planFee(base)) }}),
          split into {{ plan }} payments {{ PLAN_INTERVAL_DAYS }} days apart. + delivery at checkout (the plan fee applies to it as well). No down payment. Needs a verified account and at most two active plans.
        </template>
        <template v-else>Installments add a flat 5% plan fee, paid in equal parts every {{ PLAN_INTERVAL_DAYS }} days. Pick one to see the total.</template>
      </p>
    </fieldset>

    <label v-if="inStock && product.isInsurable" class="flex cursor-pointer items-start gap-3 rounded-lg border p-4">
      <span class="min-w-0 flex-1 text-sm">
        <span class="block font-medium">Add device insurance <span class="price text-muted-foreground font-normal">+{{ money(premium) }}</span></span>
        <span class="text-muted-foreground mt-0.5 block text-xs leading-5">12 months against damage, theft or malfunction. A claim pays out {{ money(payout) }}.</span>
      </span>
      <Switch v-model="insure" aria-label="Add device insurance" />
    </label>

    <div ref="cta">
      <Button v-if="inStock" as-child size="lg" class="w-full">
        <Link :href="buyHref" prefetch cache-for="30s">{{ plan ? `Continue with ${plan} installments` : 'Buy now' }}</Link>
      </Button>
      <Button v-else size="lg" disabled class="w-full">Out of stock</Button>
    </div>

    <ul class="text-muted-foreground space-y-2.5 border-t pt-4 text-sm">
      <li v-if="product.warrantyMonths > 0" class="flex items-center gap-2.5"><ShieldCheck class="size-4 shrink-0" aria-hidden="true" /> {{ product.warrantyMonths }}-month warranty, registered when delivered</li>
      <li class="flex items-center gap-2.5"><Truck class="size-4 shrink-0" aria-hidden="true" /> Delivery fee depends on your zone and shows at checkout</li>
    </ul>

    <Transition enter-active-class="transition duration-200 ease-out" enter-from-class="translate-y-full" leave-active-class="transition duration-150 ease-in" leave-to-class="translate-y-full">
      <div v-if="showBar" class="bg-background/95 fixed inset-x-0 bottom-0 z-30 border-t pb-[env(safe-area-inset-bottom)] backdrop-blur lg:hidden">
        <div class="mx-auto flex max-w-7xl items-center gap-3 px-4 py-2.5 sm:px-6">
          <div class="min-w-0 flex-1">
            <template v-if="plan">
              <p class="price text-sm font-semibold">{{ plan }} × {{ money(perInstallment(base, plan)) }} <span class="text-muted-foreground font-normal">every 2 weeks</span></p>
              <p class="text-muted-foreground text-xs">Total {{ money(planTotal(base)) }} incl. 5% fee + delivery at checkout</p>
            </template>
            <template v-else>
              <PriceTag :price="base" size="sm" />
              <p v-if="insure" class="text-muted-foreground text-xs">incl. insurance + delivery at checkout</p>
              <InstallmentLine :amount="base" />
            </template>
          </div>
          <Button as-child size="lg" class="shrink-0"><Link :href="buyHref">{{ plan ? `${plan} installments` : 'Buy now' }}</Link></Button>
        </div>
      </div>
    </Transition>
  </div>
</template>
