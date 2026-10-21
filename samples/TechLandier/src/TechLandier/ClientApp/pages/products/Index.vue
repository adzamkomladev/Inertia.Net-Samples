<script setup lang="ts">
import { Head, InfiniteScroll } from '@inertiajs/vue3'
import { PackageSearch } from '@lucide/vue'
import ProductCard from '@/components/ProductCard.vue'
import FilterBar from '@/components/FilterBar.vue'
import PageHeader from '@/components/PageHeader.vue'
import { Skeleton } from '@/components/ui/skeleton'
import type { CategoryTile, ProductCard as ProductCardData, ProductFilterInput, ScrollPage } from '@/types'

const props = defineProps<{
  products: ScrollPage<ProductCardData>
  filters: ProductFilterInput
  categories: CategoryTile[]
  searchEngine?: string
}>()

const activeCategory = props.categories.find((c) => c.slug === props.filters.category)
</script>

<template>
  <Head :title="filters.q ? `Search: ${filters.q}` : (activeCategory ? activeCategory.name : 'All products')" />

  <div class="mx-auto max-w-7xl px-4 py-8 sm:px-6 lg:px-8">
    <PageHeader
      :title="filters.q ? `Results for “${filters.q}”` : (activeCategory?.name ?? 'All products')"
      :description="
        filters.q
          ? 'Ranked by relevance across the entire catalog.'
          : (activeCategory?.tagline ?? 'Every gadget we stock — filtered your way.')
      "
    >
      <template #actions>
        <span v-if="searchEngine === 'meilisearch'" class="text-muted-foreground rounded-full border px-2.5 py-1 text-xs">Meilisearch</span>
      </template>
    </PageHeader>

    <div class="mt-6">
      <FilterBar :filters="filters" :categories="categories" />
    </div>

    <div class="mt-6" role="status" aria-live="polite">
      <InfiniteScroll data="products" only-next preserve-url :buffer="500">
        <template #default="{ loadingNext }">
          <div v-if="products.data.length" class="grid grid-cols-1 gap-4 sm:grid-cols-2 lg:grid-cols-3 xl:grid-cols-4">
            <ProductCard v-for="p in products.data" :key="p.id" :product="p" />
          </div>
          <div v-if="loadingNext" class="mt-4 grid grid-cols-1 gap-4 sm:grid-cols-2 lg:grid-cols-3 xl:grid-cols-4" aria-label="Loading more products">
            <Skeleton v-for="i in 4" :key="i" class="h-80 rounded-2xl" />
            <span class="sr-only">Loading more products…</span>
          </div>

          <!-- Empty catalogue state -->
          <div v-if="!products.data.length" class="mt-2 rounded-3xl border border-dashed">
            <div class="flex flex-col items-center gap-4 px-6 py-16 text-center">
              <span class="bg-card text-muted-foreground flex size-14 items-center justify-center rounded-2xl border shadow-xs">
                <PackageSearch class="size-6" :stroke-width="1.5" aria-hidden="true" />
              </span>
              <div class="max-w-sm space-y-1.5">
                <p class="font-display text-lg font-semibold">No gadgets match</p>
                <p class="text-muted-foreground text-sm leading-6">
                  Nothing matched{{ filters.q ? ` “${filters.q}”` : '' }} with these filters. Loosen the price range or browse everything.
                </p>
              </div>
              <a href="/products" class="text-brand rounded-full border px-4 py-2 text-sm font-medium transition-colors hover:bg-accent">Clear all filters</a>
            </div>
          </div>
        </template>
      </InfiniteScroll>
    </div>
  </div>
</template>
