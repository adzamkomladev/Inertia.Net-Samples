<script setup lang="ts">
import { Link } from '@inertiajs/vue3'
import PriceTag from '@/components/PriceTag.vue'
import ProductArt from '@/components/ProductArt.vue'
import { money } from '@/lib/money'
import type { OrderView } from '@/types'

defineProps<{ order: OrderView }>()
const art = (s: string, i: number) => s.split('|')[i]
</script>

<!-- Line items and the money breakdown, shared by the account order page and guest tracking. -->
<template>
  <section class="surface p-5" aria-labelledby="items-h">
    <h2 id="items-h" class="text-base font-semibold">Items</h2>
    <ul class="mt-3 divide-y">
      <li v-for="item in order.items" :key="item.id" class="flex items-center gap-3 py-3 first:pt-0">
        <span class="flex size-14 shrink-0 overflow-hidden rounded-lg"><ProductArt :seed="item.slug ?? item.sku" :icon="art(item.imageArt, 0)" :brand="null" rounded="rounded-lg" /></span>
        <div class="min-w-0 flex-1">
          <Link v-if="item.slug" :href="`/products/${item.slug}`" class="block truncate text-sm font-medium hover:underline">{{ item.name }}</Link>
          <span v-else class="block truncate text-sm font-medium">{{ item.name }}</span>
          <p class="text-muted-foreground text-xs">Qty {{ item.qty }} · SKU {{ item.sku }}</p>
        </div>
        <PriceTag :price="item.lineTotal" size="sm" />
      </li>
    </ul>
    <dl class="mt-3 space-y-1.5 border-t pt-4 text-sm">
      <div class="flex justify-between"><dt class="text-muted-foreground">Subtotal</dt><dd class="price">{{ money(order.subtotal) }}</dd></div>
      <div v-if="order.discountTotal > 0" class="text-success flex justify-between"><dt>Discounts</dt><dd class="price">−{{ money(order.discountTotal) }}</dd></div>
      <div class="flex justify-between"><dt class="text-muted-foreground">Delivery</dt><dd class="price">{{ order.shippingFee === 0 ? 'Free' : money(order.shippingFee) }}</dd></div>
      <div class="flex justify-between border-t pt-3 text-base font-semibold"><dt>Total</dt><dd class="price">{{ money(order.total) }}</dd></div>
    </dl>
    <p class="text-muted-foreground mt-3 text-xs capitalize">
      Paid by {{ order.paymentMethod.replace(/_/g, ' ') }}<span v-if="order.receiptNumber"> · receipt {{ order.receiptNumber }}</span>
    </p>
  </section>
</template>
