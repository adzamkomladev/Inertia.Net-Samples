<script setup lang="ts">
import { Head } from '@inertiajs/vue3'
import { Badge } from '@/components/ui/badge'
import type { ZoneRow } from '@/types'

defineProps<{ zones: ZoneRow[] }>()

const policies = [
  {
    title: 'Warranty, in plain words',
    body: 'Warranty months are printed on each product and registered automatically with your delivered order — set a serial, claim while the end date is in the future, and our team reviews within the SLA window. Approved claims ship a replacement; rejected ones say why in the decision note.',
  },
  {
    title: 'Device insurance (Spot-safe electronics)',
    body: 'Priced at 15% of the device price for 12 months, covering 90% of it against damage, theft or malfunction. Attach it at checkout or later from a delivered order item. Claims are reviewed, then paid to your mobile money — the payout lands on your dashboard and your alerts.',
  },
  {
    title: 'Buy now, pay small small',
    body: 'Any paid card or mobile-money order can move to 3, 6 or 12 installments due every 14 days with a 5% plan fee, split across the schedule. Two active plans max; a plan with overdue installment blocks new financing until it clears. Day 14 past due defaults the plan.',
  },
  {
    title: 'Deliveries & pay on delivery',
    body: 'Zones carry a fixed fee and an honest ETA. Where a zone allows pay on delivery, the rider collects the total in cash and it is reconciled on the delivery board. Pay-on-delivery is unavailable when you finance with BIPL — one payment plan per order.',
  },
  {
    title: 'Returns & complaints',
    body: 'Wrong item or product fault? Open a ticket from your account or the guest form — category, priority, and a due-by clock are attached immediately. Staff replies re-open resolved tickets automatically; unresolved threads escalate every hour.',
  },
  {
    title: 'Refunds',
    body: 'Refunds recover departmental payments to their original method. The receipt number on every paid order is your reference; payment status flips to refunded when the gateway confirms.',
  },
]
</script>

<template>
  <Head title="Policies & support" />
    <Head title="Policies, in plain words" />

  <article class="prose-ink mx-auto max-w-3xl px-4 py-10 sm:px-6">
    <p class="text-primary text-[11px] font-semibold tracking-[0.18em] uppercase">The fine print, without the fine print</p>
    <h1 class="mt-2 text-3xl font-semibold tracking-tight">Everything you'd ask before buying.</h1>

    <div class="not-prose mt-8 flex flex-col gap-4">
      <section v-for="(p, i) in policies" :key="i" class="bg-card shadow-card rounded-2xl border p-5">
        <h2 class="font-display text-base font-semibold">{{ p.title }}</h2>
        <p class="text-muted-foreground mt-2 text-sm leading-6">{{ p.body }}</p>
      </section>
    </div>

    <h2 class="mt-12 text-xl font-semibold tracking-tight">Delivery zones</h2>
    <p class="text-muted-foreground mt-2 text-[15px] leading-7">
      Fees are per order, collected at checkout (or in cash by the rider where noted). A vehicle number and phone
      ride along with every dispatch on your order page.
    </p>
    <div class="not-prose mt-6 bg-card shadow-card overflow-hidden rounded-2xl border">
      <ul class="divide-y">
        <li v-for="z in zones" :key="z.name" class="flex flex-wrap items-center gap-x-4 gap-y-1 px-4 py-3 text-sm sm:px-5">
          <span class="font-medium">{{ z.name }}</span>
          <span class="text-muted-foreground">{{ z.etaText }}</span>
          <Badge v-if="z.allowPayOnDelivery" variant="outline" class="ml-auto">cash on delivery OK</Badge>
          <span class="font-mono ml-auto tabular-nums">${{ z.fee.toFixed(2) }}</span>
        </li>
      </ul>
    </div>
  </article>
</template>
