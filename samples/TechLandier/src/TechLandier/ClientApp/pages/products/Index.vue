<script setup lang="ts">
import { computed } from 'vue'
import { Head, InfiniteScroll, Link } from '@inertiajs/vue3'
import { PackageSearch } from '@lucide/vue'
import EmptyState from '@/components/EmptyState.vue'
import FilterBar from '@/components/FilterBar.vue'
import PageHeader from '@/components/PageHeader.vue'
import ProductCard from '@/components/ProductCard.vue'
import ProductGridSkeleton from '@/components/ProductGridSkeleton.vue'
import { Button } from '@/components/ui/button'
import type { CategoryTile, ProductCard as ProductCardData, ProductFilterInput, ScrollPage } from '@/types'

const props = defineProps<{
  products: ScrollPage<ProductCardData>
  filters: ProductFilterInput
  categories: CategoryTile[]
}>()

const activeCategory = computed(() => props.categories.find((c) => c.slug === props.filters.category))
</script>

<template>
  <Head :title="filters.q ? `Search: ${filters.q}` : (activeCategory ? activeCategory.name : 'All products')" />

  <div class="mx-auto max-w-7xl px-4 py-8 sm:px-6 lg:px-8">
    <PageHeader
      :title="filters.q ? `Results for “${filters.q}”` : (activeCategory?.name ?? 'All products')"
      :description="filters.q ? 'Ranked by relevance across the whole catalogue.' : activeCategory?.tagline"
    />

    <div class="mt-6">
      <FilterBar :filters="filters" :categories="categories" />
    </div>

    <div class="mt-8" role="status" aria-live="polite">
      <InfiniteScroll data="products" only-next preserve-url :buffer="500">
        <template #default="{ loadingNext }">
          <div v-if="products.data.length" class="grid grid-cols-2 gap-x-4 gap-y-8 md:grid-cols-3 lg:grid-cols-4 xl:gap-x-5">
            <ProductCard v-for="p in products.data" :key="p.id" :product="p" />
          </div>
          <ProductGridSkeleton v-if="loadingNext" :count="4" class="mt-8" />
          <span v-if="loadingNext" class="sr-only">Loading more products</span>

          <EmptyState
            v-if="!products.data.length"
            :icon="PackageSearch"
            title="No products match"
            :description="`Nothing matched${filters.q ? ` “${filters.q}”` : ''} with these filters. Widen the price range or clear the filters to see everything.`"
          >
            <Button as-child as="a" variant="outline"><Link href="/products" prefetch>Clear all filters</Link></Button>
          </EmptyState>
        </template>
      </InfiniteScroll>
    </div>
  </div>
</template>
