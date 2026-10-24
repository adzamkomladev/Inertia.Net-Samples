<script setup lang="ts">
import { computed, ref } from 'vue'
import { Link } from '@inertiajs/vue3'
import { Check, ShieldCheck, Truck } from '@lucide/vue'
import PriceTag from '@/components/PriceTag.vue'
import RatingStars from '@/components/RatingStars.vue'
import { Button } from '@/components/ui/button'
import { Switch } from '@/components/ui/switch'
import { money } from '@/lib/money'
import { PLAN_COUNTS, PLAN_INTERVAL_DAYS, perInstallment, planFee, planTotal } from '@/lib/installments'
import { cn } from '@/lib/utils'
import type { ProductDetail } from '@/types'

const props = defineProps<{ product: ProductDetail }>()

const inStock = computed(() => props.product.stock > 0)
const lowStock = computed(() => inStock.value && props.product.stock <= props.product.lowStockThreshold)

/** null = pay in full. */
const plan = ref<number | null>(null)
const insure = ref(false)

const premium = computed(() => Math.round(props.product.price * 0.15 * 100) / 100)
const payout = computed(() => Math.round(props.product.price * 0.9 * 100) / 100)

const buyHref = computed(() => {
  const q = new URLSearchParams()
  if (plan.value) q.set('bnpl', String(plan.value))
  if (insure.value && props.product.isInsurable) q.set('insure', 'true')
  const s = q.toString()
  return `/buy/${props.product.id}${s ? `?${s}` : ''}`
})

const options = computed(() => [
  { value: null as number | null, label: 'Pay in full', amount: money(props.product.displayPrice), note: 'today' },
  ...PLAN_COUNTS.map((n) => ({ value: n as number | null, label: `${n} installments`, amount: money(perInstallment(props.product.displayPrice, n)), note: 'every 2 weeks' })),
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
          Plan total {{ money(planTotal(product.displayPrice)) }}: the price plus a one-off 5% plan fee ({{ money(planFee(product.displayPrice)) }}),
          split into {{ plan }} payments {{ PLAN_INTERVAL_DAYS }} days apart. No down payment. Needs a verified account and at most two active plans.
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

    <Button v-if="inStock" as-child size="lg" class="w-full">
      <Link :href="buyHref" prefetch>{{ plan ? `Continue with ${plan} installments` : 'Buy now' }}</Link>
    </Button>
    <Button v-else size="lg" disabled class="w-full">Out of stock</Button>

    <ul class="text-muted-foreground space-y-2.5 border-t pt-4 text-sm">
      <li v-if="product.warrantyMonths > 0" class="flex items-center gap-2.5"><ShieldCheck class="size-4 shrink-0" aria-hidden="true" /> {{ product.warrantyMonths }}-month warranty, registered when delivered</li>
      <li class="flex items-center gap-2.5"><Truck class="size-4 shrink-0" aria-hidden="true" /> Delivery fee depends on your zone and shows at checkout</li>
    </ul>
  </div>
</template>
