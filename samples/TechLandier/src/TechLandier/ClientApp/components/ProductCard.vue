<script setup lang="ts">
import { computed } from 'vue'
import { Link } from '@inertiajs/vue3'
import { ShoppingCart, Star } from '@lucide/vue'
import ProductArt from '@/components/ProductArt.vue'
import PriceTag from '@/components/PriceTag.vue'
import { Button } from '@/components/ui/button'
import type { ProductCard as ProductCardData } from '@/types'

const props = defineProps<{ product: ProductCardData }>()

const inStock = computed(() => props.product.stock > 0)
const lowStock = computed(() => inStock.value && props.product.stock <= 5)
</script>

<!-- Storefront product tile: art, brand, rating, price, stock state. -->
<template>
  <article
    class="group bg-card relative flex flex-col overflow-hidden rounded-2xl border shadow-xs transition-all duration-300 ease-[var(--ease-out-soft)] hover:-translate-y-0.5 hover:shadow-lg"
  >
    <Link :href="`/products/${product.slug}`" prefetch class="relative block" :aria-label="product.name">
      <div class="bg-muted/40 p-4 pb-0">
        <div class="overflow-hidden rounded-xl">
          <div class="transition-transform duration-500 ease-[var(--ease-out-soft)] group-hover:scale-[1.04]">
            <ProductArt :seed="product.slug" :icon="product.icon" :brand="product.brand" :image-url="product.imageUrl" :alt="product.name" />
          </div>
        </div>
      </div>
      <span
        v-if="product.discountPercent > 0"
        class="bg-brand text-brand-foreground absolute top-3 left-3 rounded-full px-2 py-0.5 text-[11px] font-semibold"
      >
        −{{ product.discountPercent }}%
      </span>
      <span
        v-if="!inStock"
        class="bg-background/85 text-muted-foreground absolute inset-x-0 bottom-8 py-1 text-center text-xs font-medium backdrop-blur-sm"
      >
        Out of stock
      </span>
    </Link>

    <div class="flex flex-1 flex-col gap-1.5 p-4">
      <div class="flex items-center justify-between gap-2">
        <p class="text-brand text-[11px] font-semibold tracking-[0.08em] uppercase">{{ product.brand }}</p>
        <span v-if="inStock && !lowStock" class="bg-instock/25 border-instock/50 rounded-full border px-1.5 py-px text-[10px] font-semibold">
          In stock
        </span>
      </div>
      <Link :href="`/products/${product.slug}`" prefetch class="line-clamp-2 text-[15px] leading-snug font-medium underline-offset-4 hover:underline">
        {{ product.name }}
      </Link>
      <p v-if="product.ratingCount > 0" class="text-muted-foreground flex items-center gap-1 text-xs">
        <Star class="fill-warning text-warning size-3.5" aria-hidden="true" />
        <span class="price">{{ product.ratingAvg.toFixed(1) }}</span>
        <span aria-hidden="true">·</span>
        {{ product.ratingCount.toLocaleString() }} reviews
      </p>
      <div class="mt-auto flex items-end justify-between gap-2 pt-2">
        <PriceTag :price="product.displayPrice" :compare-at="product.compareAtPrice" :discount-percent="product.discountPercent" size="sm" />
        <Button v-if="inStock" as-child size="icon-sm" class="rounded-xl" :aria-label="`Buy ${product.name}`">
          <Link :href="`/buy/${product.id}`" prefetch><ShoppingCart class="size-4" /></Link>
        </Button>
      </div>
    </div>
  </article>
</template>
