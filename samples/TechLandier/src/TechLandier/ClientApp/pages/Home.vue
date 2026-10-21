<script setup lang="ts">
import { Deferred, Head, Link } from '@inertiajs/vue3'
import { ArrowRight, BadgeCheck, ChevronRight, CreditCard, ShieldCheck, Sparkles, Truck } from '@lucide/vue'
import ProductCard from '@/components/ProductCard.vue'
import { Button } from '@/components/ui/button'
import { Skeleton } from '@/components/ui/skeleton'
import { categoryIcon } from '@/lib/icons'
import type { CategoryTile, ProductCard as ProductCardData } from '@/types'

defineProps<{
  categories?: CategoryTile[]
  deals?: ProductCardData[]
  arrivals?: ProductCardData[]
}>()

const trust = [
  { icon: CreditCard, title: 'BNPL, 0% down', text: 'Split into 3, 6 or 12 easy installments.' },
  { icon: ShieldCheck, title: 'Warranty + insurance', text: 'Coverage on every flagship device.' },
  { icon: Truck, title: 'Same-day delivery', text: 'Riders across all active zones.' },
  { icon: BadgeCheck, title: 'Genuine stock', text: 'Sealed units, local warranty cards.' },
]
</script>

<template>
  <Head title="Premium tech, on your schedule" />

  <!-- Hero -->
  <section class="hero-gradient relative overflow-hidden text-white">
    <div class="mx-auto max-w-7xl px-4 py-20 sm:px-6 sm:py-28 lg:px-8">
      <div class="max-w-2xl">
        <p class="inline-flex items-center gap-2 rounded-full bg-white/10 px-3 py-1 text-xs font-semibold tracking-wide uppercase">
          <Sparkles class="size-3.5" aria-hidden="true" />
          New season, new gear
        </p>
        <h1 class="font-display mt-5 text-4xl font-bold tracking-tight text-balance sm:text-6xl">
          Premium tech,
          <span class="bg-gradient-to-r from-lime-300 to-emerald-300 bg-clip-text text-transparent">on your schedule.</span>
        </h1>
        <p class="mt-5 max-w-xl text-base/7 text-white/75 sm:text-lg">
          Flagship phones, studios and consoles — buy now, pay in 3, 6 or 12 installments.
          Warranties, insurance and same-day rider delivery included.
        </p>
        <div class="mt-8 flex flex-wrap gap-3">
          <Button as-child size="lg" class="glow rounded-2xl font-semibold">
            <Link href="/products" prefetch>Shop all products <ArrowRight class="size-4" /></Link>
          </Button>
          <Button as-child size="lg" variant="outline" class="rounded-2xl border-white/25 bg-white/5 text-white hover:bg-white/15 hover:text-white">
            <Link href="/deals" prefetch>See today's deals</Link>
          </Button>
        </div>
      </div>
    </div>
    <div class="absolute inset-0 bg-[radial-gradient(60%_50%_at_80%_10%,rgba(132,204,22,0.18),transparent)]" aria-hidden="true" />
  </section>

  <!-- Trust strip -->
  <section class="mx-auto max-w-7xl px-4 pt-12 sm:px-6 lg:px-8">
    <div class="bg-border grid gap-px overflow-hidden rounded-3xl border shadow-xs *:bg-card sm:grid-cols-2 lg:grid-cols-4">
      <div v-for="item in trust" :key="item.title" class="flex items-start gap-3 p-5">
        <span class="bg-brand/10 text-brand flex size-10 shrink-0 items-center justify-center rounded-xl" aria-hidden="true">
          <component :is="item.icon" class="size-5" :stroke-width="1.75" />
        </span>
        <div>
          <p class="text-sm font-semibold">{{ item.title }}</p>
          <p class="text-muted-foreground text-sm">{{ item.text }}</p>
        </div>
      </div>
    </div>
  </section>

  <!-- Categories + rails (one deferred group server-side) -->
  <Deferred :data="['categories', 'deals', 'arrivals']">
    <template #fallback>
      <div class="mx-auto max-w-7xl px-4 pt-14 sm:px-6 lg:px-8" aria-busy="true">
        <Skeleton class="h-8 w-56" />
        <div class="mt-6 grid grid-cols-2 gap-3 sm:grid-cols-3 lg:grid-cols-5">
          <Skeleton v-for="i in 5" :key="i" class="h-36 rounded-2xl" />
        </div>
        <Skeleton class="mt-14 h-8 w-56" />
        <div class="scrollbar-none mt-6 flex gap-4 overflow-x-auto pb-2">
          <Skeleton v-for="i in 4" :key="i" class="h-80 w-64 shrink-0 rounded-2xl" />
        </div>
        <p role="status" class="sr-only">Loading the storefront…</p>
      </div>
    </template>

    <!-- Categories -->
    <section class="mx-auto max-w-7xl px-4 pt-14 sm:px-6 lg:px-8">
      <div class="flex items-end justify-between gap-4">
        <div>
          <h2 class="font-display text-2xl font-bold tracking-tight">Shop by category</h2>
          <p class="text-muted-foreground mt-1 text-sm">From studio headphones to cable-management everything.</p>
        </div>
        <Button variant="ghost" as-child class="rounded-xl">
          <Link href="/products" prefetch>All products <ChevronRight class="size-4" /></Link>
        </Button>
      </div>
      <div class="mt-6 grid grid-cols-2 gap-3 sm:grid-cols-3 lg:grid-cols-5">
        <Link
          v-for="c in categories ?? []" :key="c.id" :href="`/products?category=${c.slug}`" prefetch
          class="group bg-card rounded-2xl border p-4 shadow-xs transition-all duration-300 ease-[var(--ease-out-soft)] hover:-translate-y-0.5 hover:border-brand/40 hover:shadow-md"
        >
          <span class="bg-accent text-accent-foreground group-hover:bg-brand group-hover:text-brand-foreground flex size-10 items-center justify-center rounded-xl transition-colors" aria-hidden="true">
            <component :is="categoryIcon(c.icon)" class="size-5" :stroke-width="1.75" />
          </span>
          <p class="mt-3 text-sm font-semibold">{{ c.name }}</p>
          <p class="text-muted-foreground line-clamp-1 text-xs">{{ c.tagline }}</p>
          <p class="price text-muted-foreground mt-1 text-xs">{{ c.count }} items</p>
        </Link>
      </div>
    </section>

    <!-- Deals rail -->
    <section class="mx-auto max-w-7xl px-4 pt-14 sm:px-6 lg:px-8">
      <div class="flex items-end justify-between gap-4">
        <div>
          <h2 class="font-display text-2xl font-bold tracking-tight">Deals of the season</h2>
          <p class="text-muted-foreground mt-1 text-sm">Biggest discounts, live right now.</p>
        </div>
        <Button variant="ghost" as-child class="rounded-xl">
          <Link href="/deals" prefetch>All deals <ChevronRight class="size-4" /></Link>
        </Button>
      </div>
      <div class="scrollbar-none mt-6 flex snap-x gap-4 overflow-x-auto pb-2">
        <ProductCard v-for="p in deals ?? []" :key="p.id" :product="p" class="w-64 shrink-0 snap-start" />
        <p v-if="deals && !deals.length" class="text-muted-foreground py-10 text-sm">No deals live right now — check back soon.</p>
      </div>
    </section>

    <!-- New arrivals -->
    <section class="mx-auto max-w-7xl px-4 pt-14 pb-20 sm:px-6 lg:px-8">
      <div class="flex items-end justify-between gap-4">
        <div>
          <h2 class="font-display text-2xl font-bold tracking-tight">New arrivals</h2>
          <p class="text-muted-foreground mt-1 text-sm">Fresh drops, hot off the truck.</p>
        </div>
        <Button variant="ghost" as-child class="rounded-xl">
          <Link href="/products" prefetch>Browse <ChevronRight class="size-4" /></Link>
        </Button>
      </div>
      <div class="scrollbar-none mt-6 flex snap-x gap-4 overflow-x-auto pb-2">
        <ProductCard v-for="p in arrivals ?? []" :key="p.id" :product="p" class="w-64 shrink-0 snap-start" />
        <p v-if="arrivals && !arrivals.length" class="text-muted-foreground py-10 text-sm">Nothing new yet — new stock lands weekly.</p>
      </div>
    </section>
  </Deferred>
</template>
