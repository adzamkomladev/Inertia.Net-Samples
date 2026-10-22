<script setup lang="ts">
import { Head, Link } from '@inertiajs/vue3'
import { Button } from '@/components/ui/button'

const points = [
  { title: 'Stock you can trust', text: 'The stock count on a product page is the count in the warehouse. Stock is reserved the moment you place an order.' },
  { title: 'Installments', text: 'Split an order into 3, 6 or 12 payments, due every 14 days, for a flat 5% plan fee. Overdue payments pause new installment plans until they are paid.' },
  { title: 'Warranty and insurance', text: 'Warranty is registered automatically when your order is delivered. Optional 12-month device insurance pays out 90% of the price on damage, theft or malfunction.' },
  { title: 'Repairs', text: 'Book a repair from your account. You get a quote before any work starts, and a status update at every step.' },
  { title: 'Delivery', text: 'Pay by card, mobile money or cash on delivery where your zone allows it. Your order page shows the rider, vehicle and estimated time.' },
  { title: 'Support', text: 'Every support ticket has a priority and a response deadline. Replies to a resolved ticket reopen it.' },
]
</script>

<template>
  <Head title="About" />

  <article class="mx-auto max-w-3xl px-4 py-12 sm:px-6">
    <h1 class="text-3xl font-semibold tracking-tight text-balance sm:text-4xl">Electronics, with the payment plan built in.</h1>
    <p class="text-muted-foreground mt-4 text-lg leading-8">
      TechLandier sells phones, laptops, audio and accessories, and takes care of them after the sale: warranty, insurance and repairs all live in your account.
    </p>

    <dl class="mt-12 grid gap-x-10 gap-y-8 sm:grid-cols-2">
      <div v-for="p in points" :key="p.title" class="border-t pt-4">
        <dt class="font-medium">{{ p.title }}</dt>
        <dd class="text-muted-foreground mt-1.5 text-sm leading-6">{{ p.text }}</dd>
      </div>
    </dl>

    <div class="mt-12 flex flex-wrap gap-3">
      <Button as-child size="lg"><Link href="/products" prefetch>Browse products</Link></Button>
      <Button as-child variant="outline" size="lg"><Link href="/support/policy" prefetch>Delivery and payment policies</Link></Button>
    </div>
  </article>
</template>
