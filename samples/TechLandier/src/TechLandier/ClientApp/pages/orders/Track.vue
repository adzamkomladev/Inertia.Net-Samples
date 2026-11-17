<script setup lang="ts">
import { computed } from 'vue'
import { Head, Link, usePage } from '@inertiajs/vue3'
import { FileDown } from '@lucide/vue'
import { Button } from '@/components/ui/button'
import InstallmentPlanCard from '@/components/InstallmentPlanCard.vue'
import DeliveryCard from '@/components/orders/DeliveryCard.vue'
import OrderBadges from '@/components/orders/OrderBadges.vue'
import OrderItems from '@/components/orders/OrderItems.vue'
import OrderProgress from '@/components/orders/OrderProgress.vue'
import PageHeader from '@/components/PageHeader.vue'
import TimelineList from '@/components/TimelineList.vue'
import { fmtDate } from '@/lib/time'
import type { DeliveryView, OrderEventRow, OrderView, PlanView } from '@/types'

defineProps<{
  token: string | null
  order: OrderView
  events: OrderEventRow[]
  delivery: DeliveryView | null
  plan?: PlanView | null
}>()

// The tracking link is only the way in for signed-out visitors; signed-in owners are sent to their account page.
const signedOut = computed(() => !usePage().props.auth.user)
</script>

<template>
  <Head :title="`Tracking ${order.number}`" />

  <div class="mx-auto max-w-5xl px-4 py-8 sm:px-6">
    <PageHeader :title="`Order ${order.number}`" :description="`Placed ${fmtDate(order.createdAt, true)}`">
      <template #actions>
        <Button v-if="order.paymentStatus === 'paid' && order.receiptNumber" as-child size="sm" variant="outline">
          <a :href="`/orders/${order.number}/receipt.pdf${token ? `?t=${encodeURIComponent(token)}` : ''}`" download><FileDown aria-hidden="true" />Receipt (PDF)</a>
        </Button>
        <OrderBadges :status="order.status" :payment-status="order.paymentStatus" :plan="order.plan" />
      </template>
    </PageHeader>

    <div v-if="order.status !== 'cancelled'" class="surface mt-6 px-3 py-6 sm:px-8"><OrderProgress :status="order.status" :on-plan="!!order.plan" /></div>
    <p v-else class="bg-destructive/10 text-destructive mt-6 rounded-xl p-4 text-sm">This order was cancelled<template v-if="order.cancelReason">: {{ order.cancelReason }}</template>.</p>

    <div class="mt-6 grid gap-5 lg:grid-cols-[1fr_20rem]">
      <div class="flex flex-col gap-5">
        <!-- Installment orders: the plan, its total with the fee and every due date come before anything else. -->
        <InstallmentPlanCard v-if="plan && order.status !== 'cancelled'" :plan="plan" :order-number="order.number" :show-pay="false" />
        <OrderItems :order="order" />
        <section class="surface p-5" aria-labelledby="tl-h">
          <h2 id="tl-h" class="text-base font-semibold">Updates</h2>
          <TimelineList :events="events" class="mt-4" />
        </section>
      </div>
      <aside class="flex flex-col gap-5">
        <DeliveryCard v-if="delivery" :delivery="delivery" />
        <p v-if="signedOut" class="text-muted-foreground text-sm leading-6">
          You are viewing this order through its tracking link, so no sign-in is needed. Keep the link to come back to it.
          <template v-if="plan"> To pay installments, <Link href="/login" class="text-primary underline underline-offset-2">log in</Link>.</template>
        </p>
      </aside>
    </div>
  </div>
</template>
