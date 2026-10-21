<script setup lang="ts">
import { ref } from 'vue'
import { Star } from '@lucide/vue'
import PageHeader from '@/components/PageHeader.vue'
import StatusBadge from '@/components/StatusBadge.vue'
import PriceTag from '@/components/PriceTag.vue'
import ProductArt from '@/components/ProductArt.vue'
import TimelineList from '@/components/TimelineList.vue'
import InstallmentPlanCard from '@/components/InstallmentPlanCard.vue'
import RatingStars from '@/components/RatingStars.vue'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Head, useForm } from '@inertiajs/vue3'
import { money } from '@/lib/money'
import type { DeliveryView, OrderEventRow, OrderView, PlanView, WarrantyView, InsuranceView } from '@/types'


const props = defineProps<{
  order: OrderView
  events: OrderEventRow[]
  delivery: DeliveryView | null
  plan: PlanView | null
  warranties?: WarrantyView[]
  insurances?: InsuranceView[]
  reviewSlugs?: { slug: string; name: string }[]
}>()

const busy = ref<string | null>(null)
const payForm = useForm({})
const reviewForm = useForm({ rating: 5, title: '', body: '' })
const reviewing = ref<string | null>(null)

const setRating = (id: string) => {
  busy.value = id
  payForm.post(`/account/credit/installments/${id}/pay`, { onFinish: () => (busy.value = null) })
}

const submitReview = (slug: string) => {
  reviewForm.transform((d) => ({ ...d, slug }))
  reviewForm.post('/account/reviews', { onFinish: () => (reviewing.value = null) })
}
</script>

<template>
  <Head :title="order.number" />

  <div class="flex flex-col gap-6">
    <PageHeader :title="`Order ${order.number}`" :description="`Placed ${new Date(order.createdAt).toLocaleDateString()}`">
      <template #actions>
        <StatusBadge :status="order.status" kind="order" />
        <StatusBadge :status="order.paymentStatus" kind="payment" />
      </template>
    </PageHeader>

    <!-- items -->
    <section class="bg-card shadow-card rounded-2xl border p-5">
      <h2 class="font-display text-base font-semibold">Items</h2>
      <ul class="mt-3 divide-y">
        <li v-for="item in order.items" :key="item.id" class="flex items-center gap-3 py-3 first:pt-0">
          <span class="flex size-11 shrink-0 overflow-hidden rounded-xl"><ProductArt :seed="item.slug ?? item.sku" :icon="item.imageArt.split('|')[0]" :brand="item.imageArt.split('|')[1]" rounded="rounded-xl" /></span>
          <a v-if="item.slug" :href="`/products/${item.slug}`" class="min-w-0 flex-1 truncate text-sm font-medium hover:underline">{{ item.name }}</a>
          <span v-else class="min-w-0 flex-1 truncate text-sm font-medium">{{ item.name }}</span>
          <span class="text-muted-foreground font-mono text-xs">x{{ item.qty }}</span>
          <PriceTag :price="item.lineTotal" size="sm" />
        </li>
      </ul>
      <dl class="mt-3 space-y-1 border-t pt-3 text-sm">
        <div class="flex justify-between"><dt class="text-muted-foreground">Subtotal</dt><dd>{{ money(order.subtotal) }}</dd></div>
        <div v-if="order.discountTotal > 0" class="text-success flex justify-between"><dt>Discounts</dt><dd>-{{ money(order.discountTotal) }}</dd></div>
        <div class="flex justify-between"><dt class="text-muted-foreground">Shipping</dt><dd>{{ money(order.shippingFee) }}</dd></div>
        <div class="flex justify-between pt-2 font-semibold"><dt>Total</dt><dd><PriceTag :price="order.total" /></dd></div>
      </dl>
      <p class="text-muted-foreground mt-2 font-mono text-xs">
        {{ order.paymentMethod.replace('_', ' ') }}<span v-if="order.receiptNumber"> · receipt {{ order.receiptNumber }}</span>
      </p>
    </section>

    <div class="grid gap-5 lg:grid-cols-[1fr_22rem]">
      <div class="flex flex-col gap-5">
        <section class="bg-card shadow-card rounded-2xl border p-5">
          <h2 class="font-display text-base font-semibold">Timeline</h2>
          <TimelineList :events="props.events" class="mt-3" />
        </section>

        <!-- BNPL schedule -->
        <section v-if="plan" class="bg-card shadow-card rounded-2xl border p-5">
          <h2 class="font-display text-base font-semibold">Payment plan</h2>
          <InstallmentPlanCard :plan="plan" :order-number="order.number" :busy-id="busy" @pay="setRating" />
        </section>

        <!-- care -->
        <section v-if="warranties?.length || insurances?.length" class="bg-card shadow-card flex flex-col gap-4 rounded-2xl border p-5">
          <h2 class="font-display text-base font-semibold">Care & cover</h2>
          <div v-for="w in warranties" :key="w.id" class="border-border/60 grid gap-1 rounded-xl border p-3 sm:grid-cols-[1fr_auto] sm:items-center">
            <div>
              <p class="text-sm font-medium">{{ w.itemName }}</p>
              <p class="text-muted-foreground font-mono text-xs">{{ w.serialNumber || 'serial not set' }} · {{ w.startOn }} → {{ w.endOn }}</p>
            </div>
            <div class="flex items-center gap-2">
              <StatusBadge :status="w.status" kind="warranty" size="xs" />
              <Button v-if="w.status === 'active'" size="xs" variant="outline" :href="`/account/care/warranties`" class="rounded-full">Claim</Button>
            </div>
          </div>
          <div v-for="i in insurances" :key="i.id" class="border-border/60 grid gap-1 rounded-xl border p-3 sm:grid-cols-[1fr_auto] sm:items-center">
            <div>
              <p class="text-sm font-medium">Spot-safe · {{ i.deviceName }}</p>
              <p class="text-muted-foreground font-mono text-xs">{{ money(i.premium) }}/yr · covers {{ money(i.coverageAmount) }} · {{ i.startOn }} → {{ i.endOn }}</p>
            </div>
            <div class="flex items-center gap-2">
              <StatusBadge :status="i.status" kind="insurance" size="xs" />
              <Button v-if="i.status === 'active'" size="xs" variant="outline" :href="`/account/care/insurance`" class="rounded-full">Claim</Button>
            </div>
          </div>
        </section>

        <!-- review CTA -->
        <section v-if="reviewSlugs?.length" class="bg-card shadow-card rounded-2xl border p-5">
          <h2 class="font-display flex items-center gap-2 text-base font-semibold"><Star class="text-cta size-4" /> Rate what arrived</h2>
          <div class="mt-3 flex flex-col gap-3">
            <div v-for="r in reviewSlugs" :key="r.slug" class="border-border/60 rounded-xl border p-3">
              <div class="flex flex-wrap items-center justify-between gap-2">
                <p class="text-sm font-medium">{{ r.name }}</p>
                <Button size="xs" variant="outline" class="rounded-full" @click="reviewing = reviewing === r.slug ? null : r.slug">
                  {{ reviewing === r.slug ? 'Close' : 'Write a review' }}
                </Button>
              </div>
              <form v-if="reviewing === r.slug" class="mt-3 flex flex-col gap-3" @submit.prevent="submitReview(r.slug)">
                <RatingStars v-model:rating="reviewForm.rating" interactive size="md" />
                <input
                  v-model="reviewForm.title"
                  class="border-input bg-background rounded-xl border px-3 py-2 text-sm"
                  placeholder="Headline (optional)"
                  maxlength="80"
                />
                <textarea
                  v-model="reviewForm.body"
                  rows="3"
                  required
                  maxlength="1200"
                  class="border-input bg-background rounded-xl border px-3 py-2 text-sm"
                  placeholder="What worked, what didn't. Verified purchase reviews publish instantly."
                ></textarea>
                <div class="flex items-center gap-2">
                  <Button type="submit" size="sm" :disabled="reviewForm.processing">Publish</Button>
                  <span v-if="reviewForm.errors.body" class="text-destructive text-xs">{{ reviewForm.errors.body }}</span>
                </div>
              </form>
            </div>
          </div>
        </section>
      </div>

      <!-- delivery side card -->
      <aside>
        <div v-if="delivery" class="bg-card shadow-card sticky top-20 rounded-2xl border p-5">
          <h2 class="font-display flex items-center gap-2 text-base font-semibold"><Truck class="size-4" /> Delivery</h2>
          <div class="mt-3 space-y-2 text-sm">
            <div class="flex items-center justify-between"><span class="text-muted-foreground">Status</span><StatusBadge :status="delivery.status" kind="delivery" size="xs" /></div>
            <div class="flex justify-between"><span class="text-muted-foreground">Zone</span><span>{{ delivery.zoneName ?? '—' }}</span></div>
            <div class="flex justify-between"><span class="text-muted-foreground">ETA</span><span>{{ delivery.zoneEta ?? '—' }}</span></div>
            <div v-if="delivery.riderName" class="flex items-center justify-between"><span class="text-muted-foreground">Rider</span><span class="inline-flex items-center gap-1"><Truck class="size-3.5" /> {{ delivery.riderName }} · {{ delivery.vehicle }}</span></div>
            <div v-if="delivery.riderPhone" class="flex justify-between"><span class="text-muted-foreground">Contact</span><span class="font-mono">{{ delivery.riderPhone }}</span></div>
            <div v-if="delivery.cashCollected != null" class="flex justify-between"><span class="text-muted-foreground">Cash collected</span><span class="font-medium">{{ money(delivery.cashCollected) }}</span></div>
            <p v-if="delivery.failedReason" class="text-destructive text-xs">{{ delivery.failedReason }}</p>
          </div>
        </div>
        <div v-else class="bg-muted/40 rounded-2xl border border-dashed p-4 text-sm">
          <Badge variant="outline">No delivery</Badge>
          <p class="text-muted-foreground mt-2">This order is either pickup-by-hand or not dispatched yet.</p>
        </div>
      </aside>
    </div>
  </div>
</template>
