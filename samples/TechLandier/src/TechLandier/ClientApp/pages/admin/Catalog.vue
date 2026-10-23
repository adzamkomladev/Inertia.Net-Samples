<script setup lang="ts">
import { computed, onMounted, ref, watch } from 'vue'
import { Head, Link, router, usePage } from '@inertiajs/vue3'
import { watchDebounced } from '@vueuse/core'
import { ChevronLeft, ChevronRight, Loader2, PackageSearch, Plus, Search } from '@lucide/vue'
import PageHeader from '@/components/PageHeader.vue'
import StatusBadge from '@/components/StatusBadge.vue'
import PriceTag from '@/components/PriceTag.vue'
import ProductArt from '@/components/ProductArt.vue'
import EmptyState from '@/components/EmptyState.vue'
import RelativeTime from '@/components/RelativeTime.vue'
import SegmentedControl from '@/components/SegmentedControl.vue'
import ProductSheet from '@/components/admin/ProductSheet.vue'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import type { AdminCatalogRow, AdminCategoryRow, CatalogCounts } from '@/types'

const props = defineProps<{
  products: { data: AdminCatalogRow[]; page: number; hasMore: boolean }
  q: string
  status: string
  counts: CatalogCounts
  categories: AdminCategoryRow[]
}>()

const PAGE_SIZE = 20
const search = ref(props.q)
const status = ref(props.status)
const loading = ref(false)
const creating = ref(false)

// Old "?edit=new" links open the create sheet.
const page = usePage()
onMounted(() => (creating.value = new URL(page.url, 'http://tl.local').searchParams.get('edit') === 'new'))

/** Reloads the list with the current filters; page 1 unless a page is given. */
function load(p = 1) {
  router.get('/admin/catalog', {
    ...(search.value.trim() ? { q: search.value.trim() } : {}),
    ...(status.value !== 'all' ? { status: status.value } : {}),
    ...(p > 1 ? { page: p } : {}),
  }, { preserveState: true, preserveScroll: p === props.products.page, replace: true, only: ['products', 'q', 'status', 'counts'],
    onStart: () => (loading.value = true), onFinish: () => (loading.value = false) })
}
watchDebounced(search, () => load(), { debounce: 300 })
watch(status, () => load())

const rows = computed(() => props.products.data)
const range = computed(() => {
  const from = (props.products.page - 1) * PAGE_SIZE + 1
  return rows.value.length ? `${from}–${from + rows.value.length - 1}` : '0'
})
</script>

<template>
  <Head title="Catalog" />

  <div class="flex flex-col gap-6">
    <PageHeader title="Catalog" description="Every product, its photos, price and stock. Open one to see its details and stock history.">
      <template #actions>
        <Button @click="creating = true"><Plus /> New product</Button>
      </template>
    </PageHeader>

    <section class="surface overflow-hidden">
      <div class="flex flex-col gap-3 border-b p-3 lg:flex-row lg:items-center">
        <SegmentedControl
          v-model="status" label="Filter by status" class="scrollbar-none max-w-full overflow-x-auto"
          :options="[
            { value: 'all', label: `All · ${counts.all}` },
            { value: 'active', label: `Active · ${counts.active}` },
            { value: 'draft', label: `Draft · ${counts.draft}` },
            { value: 'low', label: `Low stock · ${counts.lowStock}` },
            { value: 'archived', label: `Archived · ${counts.archived}` },
          ]"
        />
        <div class="relative lg:ml-auto lg:w-72">
          <Search class="text-muted-foreground pointer-events-none absolute top-1/2 left-3 size-4 -translate-y-1/2" aria-hidden="true" />
          <Input v-model="search" type="search" class="pr-9 pl-9" placeholder="Name, brand or keyword…" aria-label="Search products" />
          <Loader2 v-if="loading" class="text-muted-foreground absolute top-1/2 right-3 size-4 -translate-y-1/2 animate-spin" aria-label="Loading" />
        </div>
      </div>

      <div class="transition-opacity duration-200" :class="loading && 'opacity-60'">
        <div v-if="rows.length" class="hidden grid-cols-[minmax(0,2.4fr)_minmax(0,1fr)_minmax(0,1fr)_minmax(0,0.8fr)_minmax(0,0.9fr)] gap-4 border-b px-4 py-2 text-xs font-medium text-muted-foreground md:grid">
          <span>Product</span><span>Category</span><span class="text-right">Price</span><span class="text-right">Stock</span><span class="text-right">Status</span>
        </div>
        <ul v-if="rows.length" class="divide-y">
          <li v-for="p in rows" :key="p.id">
            <Link
              :href="`/admin/catalog/${p.id}`" prefetch
              class="hover:bg-muted/40 focus-visible:bg-muted/40 group grid grid-cols-[3.5rem_minmax(0,1fr)_auto] items-center gap-x-3 gap-y-1 px-4 py-3 transition-colors md:grid-cols-[minmax(0,2.4fr)_minmax(0,1fr)_minmax(0,1fr)_minmax(0,0.8fr)_minmax(0,0.9fr)] md:gap-4"
            >
              <span class="contents md:flex md:min-w-0 md:items-center md:gap-3">
                <span class="row-span-2 block size-14 shrink-0 overflow-hidden rounded-lg border md:row-span-1 md:size-12">
                  <ProductArt :seed="p.slug" :icon="p.categoryIcon" :image-url="p.imageUrl" :alt="p.name" rounded="rounded-lg" class="transition-transform duration-300 group-hover:scale-105" />
                </span>
                <span class="min-w-0">
                  <span class="block truncate font-medium group-hover:underline">{{ p.name }}</span>
                  <span class="text-muted-foreground block truncate text-xs">{{ p.brand }} · updated <RelativeTime :iso="p.updatedAt" /></span>
                </span>
              </span>
              <span class="text-muted-foreground hidden truncate text-sm md:block">{{ p.category }}</span>
              <span class="col-start-2 md:col-start-auto md:flex md:justify-end"><PriceTag :price="p.price" :discount-percent="p.discountPercent" size="sm" /></span>
              <span class="row-start-1 col-start-3 text-right text-xs tabular-nums md:row-start-auto md:col-start-auto" :class="p.stock <= p.lowStockThreshold ? 'text-warning font-semibold' : ''">
                {{ p.stock }} <span class="text-muted-foreground font-normal">in stock</span>
              </span>
              <span class="col-start-3 flex justify-end md:col-start-auto"><StatusBadge :status="p.status" kind="product" /></span>
            </Link>
          </li>
        </ul>
        <EmptyState
          v-else :icon="PackageSearch"
          :title="search || status !== 'all' ? 'No products match' : 'No products yet'"
          :description="search || status !== 'all' ? 'Try another name or clear the filters.' : 'Add your first product with photos, price and stock.'"
        >
          <Button v-if="search || status !== 'all'" variant="outline" size="sm" @click="search = ''; status = 'all'">Clear filters</Button>
          <Button v-else @click="creating = true"><Plus /> New product</Button>
        </EmptyState>
      </div>

      <nav v-if="products.page > 1 || products.hasMore" class="flex items-center justify-between gap-3 border-t px-4 py-3" aria-label="Pagination">
        <p class="text-muted-foreground text-sm">Showing <span class="text-foreground tabular-nums">{{ range }}</span> · page {{ products.page }}</p>
        <div class="flex gap-2">
          <Button variant="outline" size="sm" :disabled="products.page <= 1 || loading" @click="load(products.page - 1)"><ChevronLeft /> Previous</Button>
          <Button variant="outline" size="sm" :disabled="!products.hasMore || loading" @click="load(products.page + 1)">Next <ChevronRight /></Button>
        </div>
      </nav>
    </section>
  </div>

  <ProductSheet v-model:open="creating" :product="null" :categories="categories" />
</template>
