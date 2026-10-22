<script setup lang="ts">
import { ref } from 'vue'
import PageHeader from '@/components/PageHeader.vue'
import StatusBadge from '@/components/StatusBadge.vue'
import DeliveryCard from '@/components/orders/DeliveryCard.vue'
import OrderItems from '@/components/orders/OrderItems.vue'
import OrderProgress from '@/components/orders/OrderProgress.vue'
import TimelineList from '@/components/TimelineList.vue'
import InstallmentPlanCard from '@/components/InstallmentPlanCard.vue'
import RatingStars from '@/components/RatingStars.vue'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { Textarea } from '@/components/ui/textarea'
import { Head, useForm, Link } from '@inertiajs/vue3'
import { fmtDate } from '@/lib/time'
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
    <PageHeader :title="`Order ${order.number}`" :description="`Placed ${fmtDate(order.createdAt, true)}`">
      <template #actions>
        <StatusBadge :status="order.status" kind="order" />
        <StatusBadge :status="order.paymentStatus" kind="payment" />
      </template>
    </PageHeader>

    <div v-if="order.status !== 'cancelled'" class="surface px-3 py-6 sm:px-8"><OrderProgress :status="order.status" /></div>
    <p v-else class="bg-destructive/10 text-destructive rounded-xl p-4 text-sm">This order was cancelled<template v-if="order.cancelReason">: {{ order.cancelReason }}</template>.</p>

    <div class="grid gap-5 lg:grid-cols-[1fr_22rem]">
      <div class="flex flex-col gap-5">
        <OrderItems :order="order" />
        <section class="surface p-5">
          <h2 class="text-base font-semibold">Updates</h2>
          <TimelineList :events="props.events" class="mt-3" />
        </section>

        <!-- BNPL schedule -->
        <InstallmentPlanCard v-if="plan" :plan="plan" :order-number="order.number" :busy-id="busy" @pay="setRating" />

        <!-- care -->
        <section v-if="warranties?.length || insurances?.length" class="surface flex flex-col gap-4 p-5">
          <h2 class="text-base font-semibold">Care & cover</h2>
          <div v-for="w in warranties" :key="w.id" class="grid gap-1 rounded-lg border p-3 sm:grid-cols-[1fr_auto] sm:items-center">
            <div>
              <p class="text-sm font-medium">{{ w.itemName }}</p>
              <p class="text-muted-foreground tabular-nums text-xs">{{ w.serialNumber || 'serial not set' }} · {{ w.startOn }} → {{ w.endOn }}</p>
            </div>
            <div class="flex items-center gap-2">
              <StatusBadge :status="w.status" kind="warranty" />
              <Button as-child v-if="w.status === 'active'" size="xs" variant="outline" class="rounded-full"><Link :href="`/account/care/warranties`" prefetch>Claim</Link></Button>
            </div>
          </div>
          <div v-for="i in insurances" :key="i.id" class="grid gap-1 rounded-lg border p-3 sm:grid-cols-[1fr_auto] sm:items-center">
            <div>
              <p class="text-sm font-medium">Insurance · {{ i.deviceName }}</p>
              <p class="text-muted-foreground tabular-nums text-xs">{{ money(i.premium) }}/yr · covers {{ money(i.coverageAmount) }} · {{ i.startOn }} → {{ i.endOn }}</p>
            </div>
            <div class="flex items-center gap-2">
              <StatusBadge :status="i.status" kind="insurance" />
              <Button as-child v-if="i.status === 'active'" size="xs" variant="outline" class="rounded-full"><Link :href="`/account/care/insurance`" prefetch>Claim</Link></Button>
            </div>
          </div>
        </section>

        <!-- review CTA -->
        <section v-if="reviewSlugs?.length" class="surface p-5">
          <h2 class="text-base font-semibold">Rate what arrived</h2>
          <div class="mt-3 flex flex-col gap-3">
            <div v-for="r in reviewSlugs" :key="r.slug" class="rounded-lg border p-3">
              <div class="flex flex-wrap items-center justify-between gap-2">
                <p class="text-sm font-medium">{{ r.name }}</p>
                <Button size="xs" variant="outline" class="rounded-full" @click="reviewing = reviewing === r.slug ? null : r.slug">
                  {{ reviewing === r.slug ? 'Close' : 'Write a review' }}
                </Button>
              </div>
              <form v-if="reviewing === r.slug" class="mt-3 flex flex-col gap-3" @submit.prevent="submitReview(r.slug)">
                <RatingStars v-model:rating="reviewForm.rating" interactive size="md" />
                <Input v-model="reviewForm.title" placeholder="Headline (optional)" maxlength="80" aria-label="Headline" />
                <Textarea v-model="reviewForm.body" rows="3" required maxlength="1200" aria-label="Your review" placeholder="What worked, what did not." />
                <div class="flex items-center gap-2">
                  <Button type="submit" size="sm" :disabled="reviewForm.processing">Publish</Button>
                  <span v-if="reviewForm.errors.body" class="text-destructive text-xs">{{ reviewForm.errors.body }}</span>
                </div>
              </form>
            </div>
          </div>
        </section>
      </div>

      <aside class="flex flex-col gap-5 lg:sticky lg:top-24 lg:self-start">
        <DeliveryCard v-if="delivery" :delivery="delivery" />
        <p v-else class="text-muted-foreground text-sm leading-6">No delivery yet. A rider is assigned once the order is packed.</p>
      </aside>
    </div>
  </div>
</template>
