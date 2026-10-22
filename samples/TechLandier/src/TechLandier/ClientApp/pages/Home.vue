<script setup lang="ts">
import { computed } from 'vue'
import { Head, Link } from '@inertiajs/vue3'
import InstallmentLine from '@/components/InstallmentLine.vue'
import PriceTag from '@/components/PriceTag.vue'
import ProductArt from '@/components/ProductArt.vue'
import ProductCard from '@/components/ProductCard.vue'
import Rail from '@/components/Rail.vue'
import { Button } from '@/components/ui/button'
import { categoryIcon } from '@/lib/icons'
import type { CategoryTile, ProductCard as ProductCardData } from '@/types'

const props = defineProps<{
  categories?: CategoryTile[]
  deals?: ProductCardData[]
  arrivals?: ProductCardData[]
}>()

// The hero is the best live deal; the rail below skips it so it is not shown twice.
const hero = computed(() => props.deals?.[0] ?? null)
const restDeals = computed(() => props.deals?.slice(1) ?? [])

const benefits = [
  { title: 'Pay in 3, 6 or 12 installments', text: 'Every price shows what it costs per installment, with a flat 5% plan fee and no down payment.' },
  { title: 'Card, mobile money or on delivery', text: 'Choose your delivery zone at checkout to see the fee and whether cash on delivery is available.' },
  { title: 'Warranty on every device', text: 'Registered automatically when your order is delivered. Insurance is optional on eligible devices.' },
]
</script>

<template>
  <Head title="Phones, laptops and audio, pay your way" />

    <!-- Hero: the top deal, with the installment price up front -->
    <section v-if="hero" class="mx-auto max-w-7xl px-4 pt-6 sm:px-6 lg:px-8" aria-labelledby="hero-title">
      <div class="bg-card grid overflow-hidden rounded-xl md:grid-cols-2">
        <div class="order-2 flex flex-col justify-center gap-5 p-6 sm:p-10 md:order-1 lg:p-14">
          <p class="text-primary text-sm font-medium">
            Deal of the week<span v-if="hero.discountPercent > 0"> · {{ hero.discountPercent }}% off</span>
          </p>
          <div class="space-y-2">
            <p class="text-muted-foreground text-sm">{{ hero.brand }}</p>
            <h1 id="hero-title" class="text-3xl font-semibold tracking-tight text-balance sm:text-5xl">{{ hero.name }}</h1>
          </div>
          <div class="space-y-1">
            <PriceTag :price="hero.displayPrice" :compare-at="hero.compareAtPrice" :discount-percent="0" size="lg" />
            <InstallmentLine :amount="hero.displayPrice" />
          </div>
          <div class="flex flex-wrap gap-3">
            <Button as-child size="lg"><Link :href="`/products/${hero.slug}`" prefetch>View device</Link></Button>
            <Button as-child size="lg" variant="outline"><Link href="/deals" prefetch>All deals</Link></Button>
          </div>
        </div>
        <Link :href="`/products/${hero.slug}`" prefetch class="order-1 block md:order-2" :aria-label="hero.name" tabindex="-1">
          <ProductArt :seed="hero.slug" :icon="hero.icon" :brand="hero.brand" :image-url="hero.imageUrl" :alt="hero.name" rounded="rounded-none" class="max-h-[22rem] w-full md:max-h-none" />
        </Link>
      </div>
    </section>

    <!-- Categories: quick pills, not tiles -->
    <nav v-if="categories?.length" class="mx-auto max-w-7xl px-4 pt-6 sm:px-6 lg:px-8" aria-label="Shop by category">
      <ul class="scrollbar-none -mx-4 flex gap-2 overflow-x-auto px-4 sm:mx-0 sm:flex-wrap sm:px-0">
        <li v-for="c in categories" :key="c.id" class="shrink-0">
          <Link :href="`/products?category=${c.slug}`" prefetch class="bg-card hover:bg-muted flex h-11 items-center gap-2 rounded-full px-4 text-sm font-medium transition-colors">
            <component :is="categoryIcon(c.icon)" class="text-muted-foreground size-4" :stroke-width="1.75" aria-hidden="true" />
            {{ c.name }}
          </Link>
        </li>
      </ul>
    </nav>

    <Rail v-if="restDeals.length" title="Deals" href="/deals" link-label="All deals">
      <ProductCard v-for="p in restDeals" :key="p.id" :product="p" class="w-56 shrink-0 snap-start sm:w-60" />
    </Rail>
    <Rail v-if="arrivals?.length" title="New arrivals" href="/products" link-label="Browse all">
      <ProductCard v-for="p in arrivals" :key="p.id" :product="p" class="w-56 shrink-0 snap-start sm:w-60" />
    </Rail>
    <p v-if="deals && !deals.length && arrivals && !arrivals.length" class="text-muted-foreground mx-auto max-w-7xl px-4 py-20 text-center text-sm">
      The catalogue is empty. Add products from the admin console to fill the storefront.
    </p>

    <section class="mx-auto max-w-7xl px-4 pt-16 sm:px-6 lg:px-8" aria-label="How buying works">
      <dl class="grid gap-8 border-t pt-10 md:grid-cols-3">
        <div v-for="b in benefits" :key="b.title" class="space-y-1.5">
          <dt class="font-medium">{{ b.title }}</dt>
          <dd class="text-muted-foreground text-sm leading-6">{{ b.text }}</dd>
        </div>
      </dl>
    </section>
</template>
