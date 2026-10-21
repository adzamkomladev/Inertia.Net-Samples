<script setup lang="ts">
import { Head } from '@inertiajs/vue3'
import { BadgeCheck, CreditCard, Headset, PackageCheck, ShieldCheck, Truck, Wrench, Zap } from '@lucide/vue'
import { Button } from '@/components/ui/button'
</script>

<template>
  <Head title="About TechLandier" />
    <Head title="How TechLandier works" />

  <article class="prose-ink mx-auto max-w-3xl px-4 py-10 sm:px-6">
    <p class="text-primary text-[11px] font-semibold tracking-[0.18em] uppercase">The platform</p>
    <h1 class="mt-2 text-3xl font-semibold tracking-tight">Gadgets today — pay over time if you want to.</h1>
    <p class="text-muted-foreground mt-3 text-[15px] leading-7">
      TechLandier is a gadget store first and a financing plan second. Every device we list is stock we hold:
      the inventory number on the product page is real, the warranty clock starts the day your order is delivered,
      and the same workshop that quotes your repair is behind the counter.
    </p>

    <div class="not-prose mt-10 grid gap-5 sm:grid-cols-2">
      <div class="bg-card shadow-card rounded-2xl border p-5">
        <span class="bg-cta/15 text-cta-foreground flex size-10 items-center justify-center rounded-xl"><PackageCheck class="size-5" /></span>
        <h2 class="mt-3 text-base font-semibold">Live inventory</h2>
        <p class="text-muted-foreground mt-1 text-sm leading-6">Stock is decremented the moment checkout closes — no phantom "3 left" badges. Restocks are ledgered and visible to admins in real time.</p>
      </div>
      <div class="bg-card shadow-card rounded-2xl border p-5">
        <span class="bg-cta/15 text-cta-foreground flex size-10 items-center justify-center rounded-xl"><CreditCard class="size-5" /></span>
        <h2 class="mt-3 text-base font-semibold">Buy now, pay small small</h2>
        <p class="text-muted-foreground mt-1 text-sm leading-6">Split any order into 3, 6 or 12 installments — 14 days apart, 5% total fee. Late installments pause new financing until you campus up, nothing more.</p>
      </div>
      <div class="bg-card shadow-card rounded-2xl border p-5">
        <span class="bg-cta/15 text-cta-foreground flex size-10 items-center justify-center rounded-xl"><ShieldCheck class="size-5" /></span>
        <h2 class="mt-3 text-base font-semibold">Warranty + device insurance</h2>
        <p class="text-muted-foreground mt-1 text-sm leading-6">Manufacturer warranty is registered automatically with your order. Optional 12-month device cover pays out 90% of the device price on damage, theft or malfunction.</p>
      </div>
      <div class="bg-card shadow-card rounded-2xl border p-5">
        <span class="bg-cta/15 text-cta-foreground flex size-10 items-center justify-center rounded-xl"><Wrench class="size-5" /></span>
        <h2 class="mt-3 text-base font-semibold">Repairs, in the same building</h2>
        <p class="text-muted-foreground mt-1 text-sm leading-6">Book a repair from your dashboard. Status moves received → quoted → repairing → ready with a live timeline; you get pinged at every hop.</p>
      </div>
      <div class="bg-card shadow-card rounded-2xl border p-5">
        <span class="bg-cta/15 text-cta-foreground flex size-10 items-center justify-center rounded-xl"><Truck class="size-5" /></span>
        <h2 class="mt-3 text-base font-semibold">Riders we can name</h2>
        <p class="text-muted-foreground mt-1 text-sm leading-6">Pay on delivery where the zone allows it. Your order page shows the rider, the vehicle and the ETA — no dispatcher roulette.</p>
      </div>
      <div class="bg-card shadow-card rounded-2xl border p-5">
        <span class="bg-cta/15 text-cta-foreground flex size-10 items-center justify-center rounded-xl"><Headset class="size-5" /></span>
        <h2 class="mt-3 text-base font-semibold">Complaints with an SLA</h2>
        <p class="text-muted-foreground mt-1 text-sm leading-6">Every ticket gets a priority and a due-by clock. Staff replies re-open what was closed too early; escalations are automatic, not a euphemism.</p>
      </div>
    </div>

    <div class="mt-10 flex flex-wrap items-center gap-3">
      <Button href="/products" size="lg" class="rounded-xl">
        <Zap class="size-4" /> Browse the catalog
      </Button>
      <Button href="/deals" variant="outline" size="lg" class="rounded-xl">
        <BadgeCheck class="size-4" /> See today's deals
      </Button>
    </div>
  </article>
</template>
