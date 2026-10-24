<script setup lang="ts">
import { Head } from '@inertiajs/vue3'
import DeliveryCard from '@/components/orders/DeliveryCard.vue'
import OrderItems from '@/components/orders/OrderItems.vue'
import OrderProgress from '@/components/orders/OrderProgress.vue'
import PageHeader from '@/components/PageHeader.vue'
import StatusBadge from '@/components/StatusBadge.vue'
import TimelineList from '@/components/TimelineList.vue'
import { fmtDate } from '@/lib/time'
import type { DeliveryView, OrderEventRow, OrderView } from '@/types'

defineProps<{
  token: string
  order: OrderView
  events: OrderEventRow[]
  delivery: DeliveryView | null
}>()
</script>

<template>
  <Head :title="`Tracking ${order.number}`" />

  <div class="mx-auto max-w-5xl px-4 py-8 sm:px-6">
    <PageHeader :title="`Order ${order.number}`" :description="`Placed ${fmtDate(order.createdAt, true)}`">
      <template #actions>
        <StatusBadge :status="order.status" kind="order" />
        <StatusBadge :status="order.paymentStatus" kind="payment" />
      </template>
    </PageHeader>

    <div v-if="order.status !== 'cancelled'" class="surface mt-6 px-3 py-6 sm:px-8"><OrderProgress :status="order.status" /></div>
    <p v-else class="bg-destructive/10 text-destructive mt-6 rounded-xl p-4 text-sm">This order was cancelled<template v-if="order.cancelReason">: {{ order.cancelReason }}</template>.</p>

    <div class="mt-6 grid gap-5 lg:grid-cols-[1fr_20rem]">
      <div class="flex flex-col gap-5">
        <OrderItems :order="order" />
        <section class="surface p-5" aria-labelledby="tl-h">
          <h2 id="tl-h" class="text-base font-semibold">Updates</h2>
          <TimelineList :events="events" class="mt-4" />
        </section>
      </div>
      <aside class="flex flex-col gap-5">
        <DeliveryCard v-if="delivery" :delivery="delivery" />
        <p class="text-muted-foreground text-sm leading-6">
          You are viewing this order through its tracking link, so no sign-in is needed. Keep the link to come back to it.
        </p>
      </aside>
    </div>
  </div>
</template>
