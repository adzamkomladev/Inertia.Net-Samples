<script setup lang="ts">
import { computed, ref } from 'vue'
import { Head,  router, usePage, Link } from '@inertiajs/vue3'
import { Pencil, Plus, Search } from '@lucide/vue'
import PageHeader from '@/components/PageHeader.vue'
import StatusBadge from '@/components/StatusBadge.vue'
import PriceTag from '@/components/PriceTag.vue'
import CatalogForm from '@/components/admin/CatalogForm.vue'
import DataTable from '@/components/DataTable.vue'
import ProductArt from '@/components/ProductArt.vue'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import type { AdminCatalogRow, AdminCategoryRow, EditingProduct } from '@/types'

const props = defineProps<{
  products: { data: AdminCatalogRow[]; page: number; hasMore: boolean }
  q: string
  categories: AdminCategoryRow[]
  editing: EditingProduct | null
}>()

/** Create mode is client-side: the backend only weeds an editing template for a real id. */
const page = usePage()
const editParam = computed(() => new URL(page.url, 'http://tl.local').searchParams.get('edit'))
const blank: EditingProduct = {
  id: 'new',
  name: '',
  brand: '',
  categoryId: props.categories[0]?.id ?? '',
  summary: '',
  description: '',
  price: 0,
  discountPercent: 0,
  stock: 0,
  lowStockThreshold: 5,
  warrantyMonths: 12,
  isInsurable: false,
  keywords: '',
  status: 'active',
  specsJson: '[]',
}
const rows = computed(() => props.products?.data ?? [])
const editing = computed<EditingProduct | null>(() => (editParam.value === 'new' ? blank : props.editing))
const searchTerm = ref(props.q)
const toggleCreate = () =>
  router.get('/admin/catalog', editParam.value ? {} : { edit: 'new' }, { preserveState: true })
</script>

<template>
  <Head title="Catalog" />

  <div class="flex flex-col gap-6">
    <PageHeader title="Catalog" description="Products, stock levels and pricing.">
      <template #actions>
        <Button
          size="sm"
          :variant="props.editing === null ? 'default' : 'outline'"
          @click="toggleCreate"
        >
          <Plus class="size-3.5" /> {{ editing ? 'Back to list' : 'New product' }}
        </Button>
      </template>
    </PageHeader>

    <!-- editing / create form -->
    <section v-if="editing" class="surface p-5">
      <CatalogForm :key="editing.id" :editing="editing" :categories="props.categories" />
    </section>

    <!-- list -->
    <section class="surface overflow-hidden">
      <form class="border-b flex items-center gap-2 p-3" @submit.prevent="router.get('/admin/catalog', searchTerm ? { q: searchTerm } : {}, { preserveScroll: true })">
        <div class="relative flex-1">
          <Search class="text-muted-foreground absolute top-1/2 left-3 size-4 -translate-y-1/2" />
          <Input v-model="searchTerm" class="pl-9" placeholder="Search name / brand / SKU…" />
        </div>
        <Button type="submit" size="sm" variant="outline">Search</Button>
      </form>
      <DataTable
        :columns="[
          { key: 'name', label: 'Product', primary: true },
          { key: 'category', label: 'Category' },
          { key: 'price', label: 'Price', align: 'right' as const },
          { key: 'stock', label: 'Stock', align: 'right' as const },
          { key: 'status', label: 'Status', align: 'right' as const },
          { key: 'actions', header: '', align: 'right' as const },
        ]"
        :rows="rows"
        row-key="id"
        empty-title="Nothing matches"
      >
        <template #cell-name="{ row }">
          <span class="flex items-center gap-2">
            <span class="flex size-8 shrink-0 overflow-hidden rounded-lg"><ProductArt :seed="row.slug" rounded="rounded-lg" /></span>
            <span class="min-w-0">
              <span class="block truncate font-medium">{{ row.name }}</span>
              <span class="text-muted-foreground block text-xs">{{ row.brand }}</span>
            </span>
          </span>
        </template>
        <template #cell-price="{ row }">
          <PriceTag :price="row.price" :discount-percent="row.discountPercent" size="sm" />
        </template>
        <template #cell-stock="{ row }">
          <span :class="row.stock <= row.lowStockThreshold ? 'text-warning font-medium' : ''" class="tabular-nums text-xs tabular-nums">
            {{ row.stock }} <span class="text-muted-foreground">/ {{ row.lowStockThreshold }}</span>
          </span>
        </template>
        <template #cell-status="{ row }">
          <StatusBadge :status="row.status" kind="product" />
        </template>
        <template #cell-actions="{ row }">
          <Button as-child size="xs" variant="outline" class="rounded-full"><Link :href="`/admin/catalog?edit=${row.id}`" prefetch>
            <Pencil class="size-3" /> Edit
          </Link></Button>
        </template>
      </DataTable>
    </section>
  </div>
</template>
