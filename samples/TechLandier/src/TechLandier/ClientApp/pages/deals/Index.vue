<script setup lang="ts">
import { Head, InfiniteScroll, router } from '@inertiajs/vue3'
import { Flame } from '@lucide/vue'
import ProductCard from '@/components/ProductCard.vue'
import PageHeader from '@/components/PageHeader.vue'
import { Button } from '@/components/ui/button'
import type { ProductCard as ProductCardData, ScrollPage } from '@/types'

const props = defineProps<{
  products: ScrollPage<ProductCardData>
  sort: string
}>()

const sorts = [
  { value: 'discount', label: 'Biggest discount' },
  { value: 'new', label: 'Newest' },
  { value: 'price_low', label: 'Cheapest' },
]

const setSort = (value: string) => router.get('/deals', { sort: value || undefined }, { preserveScroll: true, preserveState: true })
</script>

<template>
  <Head title="Deals" />

  <div class="mx-auto max-w-7xl px-4 py-8 sm:px-6 lg:px-8">
    <PageHeader
      title="Deals"
      description="Real discounts on the shelf right now — struck prices, no games. When one sells through, the mark-down goes with it."
    >
      <template #actions>
        <Button
          v-for="s in sorts"
          :key="s.value"
          :variant="props.sort === s.value ? 'default' : 'outline'"
          size="sm"
          class="rounded-full"
          @click="setSort(s.value)"
        >
          <Flame v-if="s.value === 'discount'" class="size-3.5" />
          {{ s.label }}
        </Button>
      </template>
    </PageHeader>

    <InfiniteScroll data="products" only-next preserve-url :buffer="500" class="mt-6">
      <template #default="{ loadingNext }">
        <div class="grid grid-cols-1 gap-5 min-[420px]:grid-cols-2 lg:grid-cols-3 xl:grid-cols-4">
          <ProductCard v-for="p in products.data" :key="p.id" :product="p" />
        </div>
        <div v-if="loadingNext" class="mt-4 flex justify-center" aria-label="Loading more deals">
          <span class="text-muted-foreground text-sm">Loading more deals…</span>
        </div>
      </template>
    </InfiniteScroll>
  </div>
</template>
