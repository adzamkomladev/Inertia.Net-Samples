<script setup lang="ts">
import { Head } from '@inertiajs/vue3'
import { ChevronDown } from '@lucide/vue'
import { money } from '@/lib/money'
import type { ZoneRow } from '@/types'

defineProps<{ zones: ZoneRow[] }>()

const policies = [
  {
    title: 'Warranty',
    body: 'Warranty months are shown on each product and start when your order is delivered. Add the serial number in your account, then open a claim while the warranty is active. Our team reviews it within the support deadline; an approved claim ships a replacement, and a rejected one says why.',
  },
  {
    title: 'Device insurance',
    body: 'Insurance costs 15% of the device price and covers 12 months. An approved claim pays out 90% of the price to your mobile money. Add it at checkout or later from a delivered order.',
  },
  {
    title: 'Installment plans',
    body: 'Orders paid by card or mobile money can be split into 3, 6 or 12 payments due every 14 days, with a flat 5% plan fee. You can have two active plans at a time. An overdue payment blocks new plans until it is paid, and a plan 14 days overdue is marked as defaulted.',
  },
  {
    title: 'Delivery and cash on delivery',
    body: 'Each zone has a fixed fee and an estimated time. Where a zone allows cash on delivery, the rider collects the total on arrival. Cash on delivery cannot be combined with installments.',
  },
  {
    title: 'Returns and complaints',
    body: 'Received the wrong item or a faulty one? Open a ticket from your account, or use the complaint form if you are not signed in. Each ticket has a priority and a response deadline, and replies to a resolved ticket reopen it.',
  },
  {
    title: 'Refunds',
    body: 'Refunds go back to the original payment method. The receipt number on your order is the reference, and the payment status changes to refunded once the payment provider confirms.',
  },
]
</script>

<template>
  <Head title="Delivery, warranty and payments" />

  <article class="mx-auto max-w-3xl px-4 py-12 sm:px-6">
    <h1 class="text-3xl font-semibold tracking-tight text-balance sm:text-4xl">Delivery, warranty and payments</h1>
    <p class="text-muted-foreground mt-4 text-lg leading-8">The answers to what people ask before they buy.</p>

    <div class="mt-10 divide-y border-y">
      <details v-for="p in policies" :key="p.title" class="group">
        <summary class="flex min-h-14 cursor-pointer list-none items-center justify-between gap-4 font-medium [&::-webkit-details-marker]:hidden">
          {{ p.title }}
          <ChevronDown class="text-muted-foreground size-4 shrink-0 transition-transform group-open:rotate-180" aria-hidden="true" />
        </summary>
        <p class="text-muted-foreground max-w-prose pb-5 text-sm leading-7">{{ p.body }}</p>
      </details>
    </div>

    <h2 class="mt-14 text-xl font-semibold tracking-tight">Delivery zones</h2>
    <p class="text-muted-foreground mt-2 text-sm leading-6">Fees are per order. You see the fee for your zone before you pay.</p>
    <ul v-if="zones.length" class="mt-4 divide-y border-y">
      <li v-for="z in zones" :key="z.name" class="flex flex-wrap items-baseline gap-x-4 gap-y-1 py-3 text-sm">
        <span class="font-medium">{{ z.name }}</span>
        <span class="text-muted-foreground">{{ z.etaText }}<template v-if="z.allowPayOnDelivery"> · cash on delivery available</template></span>
        <span class="price ml-auto font-medium">{{ z.fee === 0 ? 'Free' : money(z.fee) }}</span>
      </li>
    </ul>
    <p v-else class="text-muted-foreground mt-4 text-sm">No delivery zones are active right now.</p>
  </article>
</template>
