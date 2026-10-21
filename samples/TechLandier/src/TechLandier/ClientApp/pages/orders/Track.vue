<script setup lang="ts">
import { Head } from '@inertiajs/vue3'
import { CheckCircle2, Circle, CircleDashed, MapPin, Package, Phone, Truck, Wallet } from '@lucide/vue'
import StatusBadge from '@/components/StatusBadge.vue'
import PageHeader from '@/components/PageHeader.vue'
import TimelineList from '@/components/TimelineList.vue'
import PriceTag from '@/components/PriceTag.vue'
import ProductArt from '@/components/ProductArt.vue'
import { Badge } from '@/components/ui/badge'
import { money } from '@/lib/money'
import { fmtDate } from '@/lib/time'
import type { DeliveryView, OrderEventRow, OrderView } from '@/types'

const props = defineProps<{
  token: string
  order: OrderView
  events: OrderEventRow[]
  delivery: DeliveryView | null
}>()

const milestones = ['pending', 'paid', 'processing', 'shipped', 'delivered'] as const
const reached = (status: string) => {
  const at = milestones.indexOf(status as (typeof milestones)[number])
  const current = milestones.indexOf(props.order.status as (typeof milestones)[number])
  return at <= current && props.order.status !== 'cancelled'
}
</script>

<template>
  <Head :title="`${order.number} · tracking`" />
    <Head :title="`Track ${order.number}`" />

  <div class="mx-auto max-w-4xl px-4 py-8 sm:px-6">
    <PageHeader :title="`Order ${order.number}`" :description="`Placed ${fmtDate(order.createdAt, true)}`">
      <template #actions>
        <StatusBadge :status="order.status" kind="order" />
        <StatusBadge :status="order.paymentStatus" kind="payment" />
      </template>
    </PageHeader>

    <!-- milestone strip -->
    <div v-if="order.status !== 'cancelled'" class="bg-card shadow-card mt-6 flex items-center justify-between gap-1 rounded-2xl border p-4 sm:p-5">
      <template v-for="(m, i) in milestones" :key="m">
        <div class="flex flex-col items-center gap-1.5 text-center">
          <CheckCircle2 v-if="reached(m)" class="text-success size-5" />
          <Circle v-else-if="reached(milestones[i - 1])" class="text-cta size-5 animate-pulse" />
          <CircleDashed v-else class="text-muted-foreground/50 size-5" />
          <span class="text-muted-foreground text-[11px] font-medium capitalize">{{ m === 'processing' ? 'packing' : m }}</span>
        </div>
        <div v-if="i < milestones.length - 1" class="bg-border mx-1 h-px flex-1" :class="reached(milestones[i + 1]) ? 'bg-success/60' : ''" />
      </template>
    </div>

    <div class="mt-6 grid gap-5 lg:grid-cols-[1fr_20rem]">
      <section class="flex flex-col gap-5">
        <!-- items -->
        <div class="bg-card shadow-card rounded-2xl border p-5">
          <h2 class="font-display flex items-center gap-2 text-base font-semibold"><Package class="size-4" /> Items</h2>
          <ul class="mt-3 divide-y">
            <li v-for="item in order.items" :key="item.id" class="flex items-center gap-3 py-3 first:pt-0">
              <span class="flex size-10 shrink-0 overflow-hidden rounded-xl"><ProductArt :seed="item.slug ?? item.sku" :icon="item.imageArt.split('|')[0]" :brand="item.imageArt.split('|')[1]" rounded="rounded-xl" /></span>
              <div class="min-w-0 flex-1">
                <component :is="item.slug ? 'a' : 'span'" :href="item.slug ? `/products/${item.slug}` : undefined" class="truncate text-sm font-medium hover:underline">
                  {{ item.name }}
                </component>
                <p class="text-muted-foreground font-mono text-xs">SKU {{ item.sku }} · x{{ item.qty }}</p>
              </div>
              <PriceTag :price="item.unitPrice" size="sm" />
            </li>
          </ul>
          <dl class="mt-3 border-t pt-3 text-sm">
            <div class="flex justify-between py-0.5"><dt class="text-muted-foreground">Subtotal</dt><dd>{{ money(order.subtotal) }}</dd></div>
            <div v-if="order.discountTotal > 0" class="flex justify-between py-0.5 text-success"><dt>Discounts</dt><dd>-{{ money(order.discountTotal) }}</dd></div>
            <div class="flex justify-between py-0.5"><dt class="text-muted-foreground">Shipping</dt><dd>{{ money(order.shippingFee) }}</dd></div>
            <div class="mt-2 flex justify-between border-t pt-2 font-semibold"><dt>Total</dt><dd><PriceTag :price="order.total" /></dd></div>
            <p v-if="order.receiptNumber" class="text-muted-foreground mt-2 text-xs">Receipt {{ order.receiptNumber }}</p>
          </dl>
        </div>

        <!-- timeline -->
        <div class="bg-card shadow-card rounded-2xl border p-5">
          <h2 class="font-display text-base font-semibold">Timeline</h2>
          <TimelineList :events="events" class="mt-3" />
        </div>
      </section>

      <aside class="flex flex-col gap-5">
        <!-- delivery -->
        <div v-if="delivery" class="bg-card shadow-card rounded-2xl border p-5">
          <h2 class="font-display flex items-center gap-2 text-base font-semibold"><Truck class="size-4" /> Delivery</h2>
          <div class="mt-3 space-y-2 text-sm">
            <div class="flex items-center justify-between"><span class="text-muted-foreground">Status</span><StatusBadge :status="delivery.status" kind="delivery" size="xs" /></div>
            <div class="flex items-center justify-between"><span class="text-muted-foreground">Zone</span><span class="inline-flex items-center gap-1"><MapPin class="size-3.5" /> {{ delivery.zoneName ?? '—' }}</span></div>
            <div class="flex items-center justify-between"><span class="text-muted-foreground">ETA</span><span>{{ delivery.zoneEta ?? '—' }}</span></div>
            <div v-if="delivery.riderName" class="flex items-center justify-between">
              <span class="text-muted-foreground">Rider</span>
              <span class="inline-flex items-center gap-1"><Truck class="size-3.5" /> {{ delivery.riderName }} · {{ delivery.vehicle }}</span>
            </div>
            <div v-if="delivery.riderPhone" class="flex items-center justify-between"><span class="text-muted-foreground">Contact</span><span class="inline-flex items-center gap-1"><Phone class="size-3.5" /> {{ delivery.riderPhone }}</span></div>
            <div v-if="delivery.cashCollected != null" class="flex items-center justify-between"><span class="text-muted-foreground">Cash collected</span><span class="inline-flex items-center gap-1 font-medium"><Wallet class="size-3.5" /> {{ money(delivery.cashCollected) }}</span></div>
            <p v-if="delivery.failedReason" class="text-destructive text-xs">{{ delivery.failedReason }}</p>
          </div>
        </div>

        <!-- token notice -->
        <div class="bg-muted/40 rounded-2xl border border-dashed p-4">
          <Badge variant="outline" class="mb-2">Guest tracking</Badge>
          <p class="text-muted-foreground text-xs leading-6">
            This link works without an account while the tracking token is in the URL. Bookmark it, or sign in with the
            buying account for the full history.
          </p>
        </div>
      </aside>
    </div>
  </div>
</template>
