<script setup lang="ts">
import { Minus, Plus } from '@lucide/vue'
import PriceTag from '@/components/PriceTag.vue'
import ProductArt from '@/components/ProductArt.vue'
import { Button } from '@/components/ui/button'
import { money } from '@/lib/money'
import type { ProductDetail } from '@/types'

defineProps<{
  product: ProductDetail
  qty: number
  /** null until a delivery zone is picked. */
  shipping: number | null
  premium: number
  total: number
  installments: number | null
  perInstallment: number
  planFee: number
}>()
const emit = defineEmits<{ 'update:qty': [qty: number] }>()
</script>

<!-- Order summary: item, quantity stepper and every line that makes up the total. -->
<template>
  <div class="surface space-y-5 p-5 sm:p-6">
    <h2 class="text-base font-semibold">Order summary</h2>
    <div class="flex gap-4">
      <div class="w-20 shrink-0 overflow-hidden rounded-lg">
        <ProductArt :seed="product.slug" :icon="product.icon" :brand="null" :image-url="product.imageUrl" :alt="product.name" rounded="rounded-none" />
      </div>
      <div class="min-w-0 space-y-1">
        <p class="text-muted-foreground text-xs">{{ product.brand }}</p>
        <p class="line-clamp-2 text-sm font-medium">{{ product.name }}</p>
        <PriceTag :price="product.displayPrice" :compare-at="product.compareAtPrice" size="sm" />
      </div>
    </div>

    <div class="flex items-center justify-between gap-3 text-sm">
      <span id="qty-label" class="text-muted-foreground">Quantity</span>
      <div class="flex items-center gap-1" role="group" aria-labelledby="qty-label">
        <Button type="button" variant="outline" size="icon-sm" aria-label="Decrease quantity" :disabled="qty <= 1" @click="emit('update:qty', qty - 1)"><Minus /></Button>
        <output class="price w-9 text-center font-medium" aria-live="polite">{{ qty }}</output>
        <Button type="button" variant="outline" size="icon-sm" aria-label="Increase quantity" :disabled="qty >= Math.min(10, product.stock)" @click="emit('update:qty', qty + 1)"><Plus /></Button>
      </div>
    </div>

    <dl class="space-y-2.5 border-t pt-4 text-sm">
      <div class="flex justify-between gap-4"><dt class="text-muted-foreground">Items ({{ qty }})</dt><dd class="price">{{ money(product.displayPrice * qty) }}</dd></div>
      <div class="flex justify-between gap-4"><dt class="text-muted-foreground">Delivery</dt><dd class="price">{{ shipping === null ? 'Pick a zone' : shipping === 0 ? 'Free' : money(shipping) }}</dd></div>
      <div v-if="premium > 0" class="flex justify-between gap-4"><dt class="text-muted-foreground">Device insurance</dt><dd class="price">{{ money(premium) }}</dd></div>
      <div v-if="installments" class="flex justify-between gap-4"><dt class="text-muted-foreground">Installment plan fee (5%)</dt><dd class="price">{{ money(planFee) }}</dd></div>
      <div class="flex justify-between gap-4 border-t pt-3 text-base font-semibold">
        <dt>{{ installments ? 'Plan total' : 'Total' }}</dt><dd class="price">{{ money(installments ? total + planFee : total) }}</dd>
      </div>
      <div v-if="installments" class="bg-accent text-accent-foreground flex justify-between gap-4 rounded-lg px-3 py-2.5 font-medium">
        <dt>{{ installments }} payments of</dt><dd class="price">{{ money(perInstallment) }}</dd>
      </div>
    </dl>
    <slot />
  </div>
</template>
