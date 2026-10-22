<script setup lang="ts">
import { computed } from 'vue'
import { router } from '@inertiajs/vue3'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select'
import { Switch } from '@/components/ui/switch'
import { cn } from '@/lib/utils'
import type { CategoryTile, ProductFilterInput } from '@/types'

const props = defineProps<{ filters: ProductFilterInput; categories: CategoryTile[]; href?: string }>()

const href = props.href ?? '/products'

const sortOptions = [
  { value: 'new', label: 'Newest' },
  { value: 'hlow', label: 'Price: low to high' },
  { value: 'chigh', label: 'Price: high to low' },
]

const go = (patch: Record<string, unknown>) => {
  // Null props would serialize as `min=` and fail to bind as nullable numbers, so only send real values.
  const data = Object.fromEntries(Object.entries({ ...props.filters, ...patch }).filter(([, v]) => v != null && v !== ''))
  router.get(href, data, { replace: true, preserveState: true, preserveScroll: true })
}

const dirty = computed(() => !!(props.filters.min || props.filters.max || props.filters.inStock))
const clearFilters = () => go({ min: undefined, max: undefined, inStock: undefined })
const pill = (active: boolean) =>
  cn('flex h-11 items-center rounded-full px-4 text-sm font-medium whitespace-nowrap transition-colors md:h-9', active ? 'bg-foreground text-background' : 'bg-card hover:bg-muted')
</script>

<!-- Catalogue filters: category pills, then price range, stock switch and sort. -->
<template>
  <div class="space-y-4">
    <div role="group" aria-label="Category" class="scrollbar-none -mx-4 flex gap-2 overflow-x-auto px-4 sm:mx-0 sm:flex-wrap sm:px-0">
      <button type="button" :class="pill(!filters.category)" :aria-pressed="!filters.category" @click="go({ category: undefined })">All</button>
      <button v-for="c in categories" :key="c.id" type="button" :class="pill(filters.category === c.slug)" :aria-pressed="filters.category === c.slug" @click="go({ category: c.slug })">
        {{ c.name }}
      </button>
    </div>

    <div class="flex flex-wrap items-end gap-x-5 gap-y-3">
      <div class="flex items-end gap-2">
        <div class="space-y-1">
          <Label class="text-muted-foreground text-xs" for="filter-min">Min price</Label>
          <Input id="filter-min" type="number" inputmode="numeric" min="0" class="price w-28" :model-value="filters.min ?? ''" @change="go({ min: ($event.target as HTMLInputElement).value || undefined })" />
        </div>
        <span class="text-muted-foreground pb-3 md:pb-2.5" aria-hidden="true">to</span>
        <div class="space-y-1">
          <Label class="text-muted-foreground text-xs" for="filter-max">Max price</Label>
          <Input id="filter-max" type="number" inputmode="numeric" min="0" class="price w-28" :model-value="filters.max ?? ''" @change="go({ max: ($event.target as HTMLInputElement).value || undefined })" />
        </div>
      </div>
      <label class="flex min-h-11 cursor-pointer items-center gap-2.5 text-sm">
        <Switch :model-value="filters.inStock === true" aria-label="In stock only" @update:model-value="(v) => go({ inStock: v === true ? true : undefined })" />
        In stock only
      </label>
      <Button v-if="dirty" variant="ghost" size="sm" @click="clearFilters">Clear filters</Button>
      <div class="ml-auto">
        <Label class="sr-only" for="filter-sort">Sort</Label>
        <Select :model-value="filters.sort || 'new'" @update:model-value="(v) => go({ sort: v })">
          <SelectTrigger id="filter-sort" class="w-48"><SelectValue /></SelectTrigger>
          <SelectContent>
            <SelectItem v-for="s in sortOptions" :key="s.value" :value="s.value">{{ s.label }}</SelectItem>
          </SelectContent>
        </Select>
      </div>
    </div>
  </div>
</template>
