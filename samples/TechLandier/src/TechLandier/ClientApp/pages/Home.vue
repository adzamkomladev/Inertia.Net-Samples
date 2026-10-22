<script setup lang="ts">
import { computed } from 'vue'
import { Head, Link } from '@inertiajs/vue3'
import InstallmentLine from '@/components/InstallmentLine.vue'
import PriceTag from '@/components/PriceTag.vue'
import ProductArt from '@/components/ProductArt.vue'
import ProductCard from '@/components/ProductCard.vue'
import Rail from '@/components/Rail.vue'
import { Button } from '@/components/ui/button'
import {
  ArrowUpRight,
  Headphones,
  ShieldCheck,
  Truck,
  Wallet,
  Wrench,
} from '@lucide/vue'
import StoreHours from '@/components/StoreHours.vue'
import type { StoreSettings } from '@/types/management'
import { categoryIcon } from '@/lib/icons'
import type { CategoryTile, ProductCard as ProductCardData } from '@/types'

const props = defineProps<{
  settings: StoreSettings
  categories?: CategoryTile[]
  deals?: ProductCardData[]
  arrivals?: ProductCardData[]
}>()

// The hero is the best live deal; the rail below skips it so it is not shown twice.
const hero = computed(() => props.deals?.[0] ?? null)
const restDeals = computed(() => props.deals?.slice(1) ?? [])

const benefits = [
  {
    title: 'Pay in 3, 6 or 12 installments',
    text: 'Every price shows what it costs per installment, with a flat 5% plan fee and no down payment.',
  },
  {
    title: 'Card, mobile money or on delivery',
    text: 'Choose your delivery zone at checkout to see the fee and whether cash on delivery is available.',
  },
  {
    title: 'Care for eligible devices',
    text: 'Warranty coverage is included on eligible products. Insurance is optional on eligible devices.',
  },
]
</script>

<template>
  <Head title="Phones, laptops and audio, pay your way" />

  <div
    class="border-b bg-primary/5 px-4 py-2.5 text-center text-xs font-medium sm:text-sm"
  >
    Your next upgrade, your way.
    <Link
      href="/products"
      class="ml-2 text-primary underline underline-offset-4"
      >Explore the collection</Link
    >
  </div>
  <section
    class="mx-auto max-w-7xl px-4 pt-6 sm:px-6 lg:px-8"
    aria-labelledby="hero-title"
  >
    <div class="grid gap-4 lg:grid-cols-[1.5fr_1fr]">
      <div
        class="relative isolate flex min-h-[31rem] flex-col justify-between overflow-hidden rounded-3xl bg-[#102b25] p-7 text-white sm:p-10 lg:p-12"
      >
        <div
          class="pointer-events-none absolute -right-20 -top-24 -z-10 size-96 rounded-full border-[60px] border-[#d5ff85]/10"
        ></div>
        <p
          class="flex items-center gap-2 text-xs font-semibold uppercase tracking-[.22em] text-[#d5ff85]"
        >
          <span class="size-2 rounded-full bg-[#d5ff85]"></span> The upgrade
          starts here
        </p>
        <div class="my-10 max-w-lg space-y-6">
          <h1
            id="hero-title"
            class="text-5xl font-semibold leading-[1.04] tracking-tight text-balance sm:text-6xl lg:text-7xl"
          >
            Better tech.<br /><span class="text-[#d5ff85]"
              >More possibility.</span
            >
          </h1>
          <p class="max-w-md text-sm leading-7 text-white/70 sm:text-base">
            From your daily essentials to your next big idea. Discover devices
            you will love, with flexible ways to pay and care that carries on.
          </p>
          <div class="flex flex-wrap gap-3">
            <Button
              as-child
              size="lg"
              class="bg-[#d5ff85] text-[#102b25] hover:bg-[#e4ffb5]"
              ><Link href="/products" prefetch
                >Find your upgrade
                <ArrowUpRight class="size-4" /></Link></Button
            ><Button
              as-child
              size="lg"
              variant="outline"
              class="border-white/30 bg-transparent text-white hover:bg-white/10 hover:text-white"
              ><Link href="/deals" prefetch>Shop the deals</Link></Button
            >
          </div>
        </div>
        <div
          class="flex flex-wrap gap-x-6 gap-y-3 border-t border-white/20 pt-5 text-xs text-white/75"
        >
          <span class="flex items-center gap-2"
            ><ShieldCheck class="size-4 text-[#d5ff85]" /> Warranty
            options</span
          ><span class="flex items-center gap-2"
            ><Wallet class="size-4 text-[#d5ff85]" /> Flexible payments</span
          ><span class="flex items-center gap-2"
            ><Truck class="size-4 text-[#d5ff85]" /> Local delivery</span
          >
        </div>
      </div>
      <div class="grid gap-4">
        <Link
          v-if="hero"
          :href="`/products/${hero.slug}`"
          prefetch
          class="group relative grid overflow-hidden rounded-3xl border bg-card sm:grid-cols-2 lg:grid-cols-1"
        >
          <div class="relative min-h-48 overflow-hidden lg:h-56">
            <ProductArt
              :seed="hero.slug"
              :icon="hero.icon"
              :brand="hero.brand"
              :image-url="hero.imageUrl"
              :alt="hero.name"
              rounded="rounded-none"
              class="h-full w-full transition-transform duration-500 motion-safe:group-hover:scale-105"
            /><span
              v-if="hero.discountPercent"
              class="absolute left-5 top-5 rounded-full bg-[#d5ff85] px-3 py-1 text-xs font-bold text-[#102b25]"
              >Save {{ hero.discountPercent }}%</span
            >
          </div>
          <div class="space-y-2 p-6">
            <p
              class="text-xs font-medium uppercase tracking-widest text-muted-foreground"
            >
              In the spotlight · {{ hero.brand }}
            </p>
            <h2 class="text-xl font-semibold tracking-tight">
              {{ hero.name }}
            </h2>
            <PriceTag
              :price="hero.displayPrice"
              :compare-at="hero.compareAtPrice"
              size="lg"
            /><InstallmentLine :amount="hero.displayPrice" /><span
              class="inline-flex items-center gap-2 pt-2 text-sm font-semibold text-primary"
              >Meet your next device <ArrowUpRight class="size-4"
            /></span>
          </div>
        </Link>
        <Link
          href="/products?sort=hlow"
          class="flex items-center justify-between gap-4 rounded-3xl bg-[#e6dbce] p-6 text-[#36281f]"
          ><div>
            <p class="text-xs uppercase tracking-widest">
              Big ideas. Smaller payments.
            </p>
            <h2 class="mt-2 text-xl font-semibold">
              Split it into 3, 6 or 12.
            </h2>
            <p class="mt-1 text-xs">Eligible customers · 5% plan fee</p>
          </div>
          <Wallet class="size-8 shrink-0"
        /></Link>
      </div>
    </div>
  </section>
  <nav
    v-if="categories?.length"
    class="mx-auto max-w-7xl px-4 pt-10 sm:px-6 lg:px-8"
    aria-label="Shop by category"
  >
    <div class="mb-5 flex flex-wrap items-end justify-between gap-3">
      <div>
        <p
          class="mb-1 text-xs font-medium uppercase tracking-widest text-muted-foreground"
        >
          Find your thing
        </p>
        <h2 class="text-2xl font-semibold tracking-tight">
          A little of everything. A lot to love.
        </h2>
      </div>
      <Link
        href="/products"
        class="text-sm font-medium text-primary hover:underline"
        >View all products &rarr;</Link
      >
    </div>
    <ul class="grid grid-cols-2 gap-3 sm:grid-cols-3 lg:grid-cols-5">
      <li v-for="c in categories" :key="c.id">
        <Link
          :href="`/products?category=${c.slug}`"
          prefetch
          class="group flex h-full flex-col items-start gap-4 rounded-2xl border bg-card p-5 transition-colors hover:border-primary/40 hover:bg-primary/5"
          ><span class="rounded-xl bg-muted p-3"
            ><component
              :is="categoryIcon(c.icon)"
              class="size-6 text-primary"
              :stroke-width="1.5"
              aria-hidden="true" /></span
          ><span
            class="flex w-full items-center justify-between gap-2 text-sm font-semibold"
            >{{ c.name
            }}<ArrowUpRight
              class="size-4 shrink-0 text-muted-foreground" /></span
          ><span class="text-xs text-muted-foreground"
            >Explore collection</span
          ></Link
        >
      </li>
    </ul>
  </nav>
  <Rail
    v-if="restDeals.length"
    title="Good tech. Even better prices."
    href="/deals"
    link-label="All deals"
  >
    <ProductCard
      v-for="p in restDeals"
      :key="p.id"
      :product="p"
      class="w-56 shrink-0 snap-start sm:w-60"
    />
  </Rail>
  <Rail
    v-if="arrivals?.length"
    title="Fresh finds for your everyday"
    href="/products"
    link-label="Browse all"
  >
    <ProductCard
      v-for="p in arrivals"
      :key="p.id"
      :product="p"
      class="w-56 shrink-0 snap-start sm:w-60"
    />
  </Rail>
  <p
    v-if="deals && !deals.length && arrivals && !arrivals.length"
    class="text-muted-foreground mx-auto max-w-7xl px-4 py-20 text-center text-sm"
  >
    The catalogue is empty. Add products from the admin console to fill the
    storefront.
  </p>

  <section
    class="mx-auto max-w-7xl px-4 pt-12 sm:px-6 lg:px-8"
    aria-labelledby="care-title"
  >
    <div class="grid gap-8 rounded-3xl bg-primary/5 p-7 sm:p-10 md:grid-cols-2">
      <div>
        <p class="text-xs font-semibold uppercase tracking-widest text-primary">
          More than the checkout
        </p>
        <h2 id="care-title" class="mt-3 text-3xl font-semibold tracking-tight">
          Good tech deserves<br />great care.
        </h2>
        <p class="mt-4 max-w-md text-sm leading-7 text-muted-foreground">
          Keep your devices doing what they do best. Track your orders, manage
          protection and follow a repair from first inspection to pickup.
        </p>
        <Button as-child variant="outline" class="mt-6"
          ><Link href="/account" prefetch
            >Your account & care <ArrowUpRight class="size-4" /></Link
        ></Button>
      </div>
      <div class="grid gap-4">
        <Link
          href="/account/care/warranties"
          class="flex items-start gap-4 rounded-2xl border bg-card p-5"
          ><ShieldCheck class="mt-1 size-6 shrink-0 text-primary" />
          <div>
            <h3 class="font-semibold">Protection that makes sense</h3>
            <p class="mt-1 text-sm leading-6 text-muted-foreground">
              Warranty coverage on eligible products, plus optional device
              insurance.
            </p>
          </div></Link
        ><Link
          href="/account/care/repairs"
          class="flex items-start gap-4 rounded-2xl border bg-card p-5"
          ><Wrench class="mt-1 size-6 shrink-0 text-primary" />
          <div>
            <h3 class="font-semibold">A second life for your device</h3>
            <p class="mt-1 text-sm leading-6 text-muted-foreground">
              Book a repair and follow your quote, technician and progress in
              one place.
            </p>
          </div></Link
        ><Link
          href="/support"
          class="flex items-start gap-4 rounded-2xl border bg-card p-5"
          ><Headphones class="mt-1 size-6 shrink-0 text-primary" />
          <div>
            <h3 class="font-semibold">A little help, whenever you need it</h3>
            <p class="mt-1 text-sm leading-6 text-muted-foreground">
              Questions about an order? Reach our team and track the
              conversation.
            </p>
          </div></Link
        >
      </div>
    </div>
  </section>
  <section
    class="mx-auto max-w-7xl px-4 pt-12 sm:px-6 lg:px-8"
    aria-label="How buying works"
  >
    <dl class="grid gap-6 md:grid-cols-3">
      <div
        v-for="(b, index) in benefits"
        :key="b.title"
        class="rounded-2xl border p-6"
      >
        <span class="text-xs font-semibold text-primary">0{{ index + 1 }}</span>
        <dt class="mt-4 font-semibold">{{ b.title }}</dt>
        <dd class="mt-2 text-sm leading-6 text-muted-foreground">
          {{ b.text }}
        </dd>
      </div>
    </dl>
    <StoreHours :settings="settings" class="mt-8" />
  </section>
</template>
