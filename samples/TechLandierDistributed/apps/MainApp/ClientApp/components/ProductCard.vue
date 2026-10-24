<script setup lang="ts">
import { computed } from 'vue'
import { Link } from '@inertiajs/vue3'
import InstallmentLine from '@/components/InstallmentLine.vue'
import PriceTag from '@/components/PriceTag.vue'
import ProductArt from '@/components/ProductArt.vue'
import RatingStars from '@/components/RatingStars.vue'
import type { ProductCard as ProductCardData } from '@/types'

const props = defineProps<{ product: ProductCardData }>()

const inStock = computed(() => props.product.stock > 0)
const lowStock = computed(() => inStock.value && props.product.stock <= 5)
</script>

<!-- Storefront tile: photo first, then name, price and the installment line. One link, no badge clutter. -->
<template>
  <article class="group relative flex flex-col gap-3">
    <div class="bg-card overflow-hidden rounded-xl">
      <div class="transition-transform duration-500 ease-out group-hover:scale-[1.03]" :class="!inStock && 'opacity-50'">
        <ProductArt :seed="product.slug" :icon="product.icon" :brand="product.brand" :image-url="product.imageUrl" :alt="product.name" rounded="rounded-none" />
      </div>
    </div>
    <div class="flex flex-1 flex-col gap-1">
      <p class="text-muted-foreground text-xs">{{ product.brand }}</p>
      <h3 class="text-[15px] leading-snug font-medium tracking-normal">
        <Link :href="`/products/${product.slug}`" prefetch class="line-clamp-2 rounded-sm after:absolute after:inset-0">{{ product.name }}</Link>
      </h3>
      <RatingStars v-if="product.ratingCount > 0" :rating="product.ratingAvg" :count="product.ratingCount" size="sm" />
      <div class="mt-1.5">
        <PriceTag :price="product.displayPrice" :compare-at="product.compareAtPrice" :discount-percent="product.discountPercent" size="sm" />
        <InstallmentLine v-if="inStock" :amount="product.displayPrice" />
      </div>
      <p v-if="!inStock" class="text-muted-foreground mt-1 text-xs font-medium">Out of stock</p>
      <p v-else-if="lowStock" class="text-warning mt-1 text-xs font-medium">Only {{ product.stock }} left</p>
    </div>
  </article>
</template>
