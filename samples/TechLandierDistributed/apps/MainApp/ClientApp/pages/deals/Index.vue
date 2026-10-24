<script setup lang="ts">
import { Head, InfiniteScroll, router, Link } from '@inertiajs/vue3'
import { Tag } from '@lucide/vue'
import EmptyState from '@/components/EmptyState.vue'
import PageHeader from '@/components/PageHeader.vue'
import ProductCard from '@/components/ProductCard.vue'
import ProductGridSkeleton from '@/components/ProductGridSkeleton.vue'
import { Button } from '@/components/ui/button'
import { cn } from '@/lib/utils'
import type { ProductCard as ProductCardData, ScrollPage } from '@/types'

const props = defineProps<{
  products: ScrollPage<ProductCardData>
  sort: string
}>()

const sorts = [
  { value: 'discount', label: 'Biggest discount' },
  { value: 'new', label: 'Newest' },
  { value: 'price_low', label: 'Lowest price' },
]

const setSort = (value: string) => router.get('/deals', { sort: value || undefined }, { preserveScroll: true, preserveState: true })
</script>

<template>
  <Head title="Deals" />

  <div class="mx-auto max-w-7xl px-4 py-8 sm:px-6 lg:px-8">
    <PageHeader title="Deals" description="Products with a mark-down right now. The struck price is what it cost before." />

    <div role="group" aria-label="Sort deals" class="scrollbar-none -mx-4 mt-6 flex gap-2 overflow-x-auto px-4 sm:mx-0 sm:px-0">
      <button
        v-for="s in sorts" :key="s.value" type="button" :aria-pressed="props.sort === s.value"
        :class="cn('flex h-11 items-center rounded-full px-4 text-sm font-medium whitespace-nowrap transition-colors md:h-9', props.sort === s.value ? 'bg-foreground text-background' : 'bg-card hover:bg-muted')"
        @click="setSort(s.value)"
      >
        {{ s.label }}
      </button>
    </div>

    <InfiniteScroll data="products" only-next preserve-url :buffer="500" class="mt-8">
      <template #default="{ loadingNext }">
        <div v-if="products.data.length" class="grid grid-cols-2 gap-x-4 gap-y-8 md:grid-cols-3 lg:grid-cols-4 xl:gap-x-5">
          <ProductCard v-for="p in products.data" :key="p.id" :product="p" />
        </div>
        <EmptyState v-else :icon="Tag" title="No deals right now" description="Mark-downs appear here as soon as they go live. Browse the full catalogue in the meantime.">
          <Button as-child as="a" variant="outline"><Link href="/products" prefetch>Browse all products</Link></Button>
        </EmptyState>
        <ProductGridSkeleton v-if="loadingNext" :count="4" class="mt-8" />
      </template>
    </InfiniteScroll>
  </div>
</template>
