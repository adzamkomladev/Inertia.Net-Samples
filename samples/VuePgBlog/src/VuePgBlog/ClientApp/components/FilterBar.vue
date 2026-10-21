<script setup lang="ts">
import { ref, watch } from 'vue'
import { router } from '@inertiajs/vue3'
import { Search, X } from '@lucide/vue'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select'
import { Switch } from '@/components/ui/switch'
import { useDebouncedSearch } from '@/composables/useDebouncedSearch'
import type { CategoryTile, ProductFilterInput } from '@/types'

const props = defineProps<{ filters: ProductFilterInput; categories: CategoryTile[]; href?: string }>()

const href = props.href ?? '/products'
const q = ref(props.filters.q)
useDebouncedSearch(q, href)

const sortOptions = [
  { value: 'new', label: 'Newest' },
  { value: 'chigh', label: 'Price: high → low' },
  { value: 'hlow', label: 'Price: low → high' },
]

const go = (patch: Record<string, unknown>) => {
  router.get(href, { ...props.filters, q: q.value.trim() || undefined, ...patch }, { replace: true, preserveState: true, preserveScroll: true })
}

// Sync back/forward and link-driven filter changes into the local inputs.
watch(
  () => props.filters,
  (f) => {
    if (f.q !== q.value && document.activeElement?.tagName !== 'INPUT') q.value = f.q
  },
)

const clear = () => {
  q.value = ''
  router.get(href, {}, { replace: true, preserveState: true, preserveScroll: true })
}
</script>

<!-- Catalogue filters: search, category, price range, stock toggle, sort. -->
<template>
  <section class="bg-card/60 glass rounded-2xl border p-4 shadow-xs">
    <div class="grid gap-3 md:grid-cols-[1fr_11rem_11rem]">
      <div class="relative">
        <Search class="text-muted-foreground pointer-events-none absolute top-1/2 left-3 size-4 -translate-y-1/2" aria-hidden="true" />
        <Input v-model="q" type="search" placeholder="Search products, brands…" class="pl-9" aria-label="Search products" />
        <Button
          v-if="q || filters.q"
          variant="ghost" size="icon-xs"
          class="absolute top-1/2 right-2 -translate-y-1/2 rounded-full"
          aria-label="Clear search"
          @click="clear"
        >
          <X />
        </Button>
      </div>
      <div>
        <Label class="sr-only" for="filter-category">Category</Label>
        <Select :model-value="filters.category || 'all'" @update:model-value="(v) => go({ category: v === 'all' ? undefined : v, page: undefined })">
          <SelectTrigger id="filter-category" class="w-full"><SelectValue placeholder="All categories" /></SelectTrigger>
          <SelectContent>
            <SelectItem value="all">All categories</SelectItem>
            <SelectItem v-for="c in categories" :key="c.id" :value="c.slug">{{ c.name }}</SelectItem>
          </SelectContent>
        </Select>
      </div>
      <div>
        <Label class="sr-only" for="filter-sort">Sort</Label>
        <Select :model-value="filters.sort || 'new'" @update:model-value="(v) => go({ sort: v, page: undefined })">
          <SelectTrigger id="filter-sort" class="w-full"><SelectValue /></SelectTrigger>
          <SelectContent>
            <SelectItem v-for="s in sortOptions" :key="s.value" :value="s.value">{{ s.label }}</SelectItem>
          </SelectContent>
        </Select>
      </div>
    </div>

    <div class="mt-3 flex flex-wrap items-center gap-4">
      <div class="flex items-center gap-2">
        <Label class="text-muted-foreground text-xs" for="filter-min">Min $</Label>
        <Input
          id="filter-min" type="number" min="0" class="price h-8 w-24" :model-value="filters.min ?? ''"
          @change="go({ min: ($event.target as HTMLInputElement).value || undefined, page: undefined })"
        />
      </div>
      <div class="flex items-center gap-2">
        <Label class="text-muted-foreground text-xs" for="filter-max">Max $</Label>
        <Input
          id="filter-max" type="number" min="0" class="price h-8 w-24" :model-value="filters.max ?? ''"
          @change="go({ max: ($event.target as HTMLInputElement).value || undefined, page: undefined })"
        />
      </div>
      <label class="flex cursor-pointer items-center gap-2 text-sm">
        <Switch :model-value="filters.inStock === true" @update:model-value="(v) => go({ inStock: v === true ? true : undefined, page: undefined })" />
        In stock only
      </label>
    </div>
  </section>
</template>
