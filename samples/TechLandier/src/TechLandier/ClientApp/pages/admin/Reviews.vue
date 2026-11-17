<script setup lang="ts">
import { computed } from 'vue'
import { Head, router } from '@inertiajs/vue3'
import { EyeOff, Star, Undo2 } from '@lucide/vue'
import DataTable from '@/components/DataTable.vue'
import PageHeader from '@/components/PageHeader.vue'
import StatusBadge from '@/components/StatusBadge.vue'
import EmptyState from '@/components/EmptyState.vue'
import RatingStars from '@/components/RatingStars.vue'
import { Button } from '@/components/ui/button'
import { fmtDate } from '@/lib/time'
import type { AdminReviewRow, NumberedPage } from '@/types'

const props = defineProps<{ reviews: NumberedPage<AdminReviewRow>; state: string }>()
const rows = computed(() => props.reviews?.data ?? [])

const filters = [
  { id: 'published', label: 'Published' },
  { id: 'hidden', label: 'Hidden' },
]
const toggle = (r: AdminReviewRow) => {
  const state = r.status === 'published' ? 'hidden' : 'published'
  // Optimistic: the badge flips at once and rolls back if the request fails.
  router.optimistic<{ reviews: NumberedPage<AdminReviewRow> }>((current) => ({
    reviews: { ...current.reviews, data: current.reviews.data.map((x) => (x.id === r.id ? { ...x, status: state } : x)) },
  })).post(`/admin/reviews/${r.id}`, { state }, { preserveScroll: true })
}
</script>

<template>
  <Head title="Reviews" />

  <div class="flex flex-col gap-6">
    <PageHeader title="Reviews" description="Reviews from verified purchases. Hide a review that breaks the rules, or show it again.">
      <template #actions>
        <Button
          v-for="f in filters"
          :key="f.id"
          size="xs"
          :variant="props.state === f.id ? 'default' : 'outline'"
          class="rounded-full"
          @click="router.get('/admin/reviews', f.id === 'published' ? {} : { state: f.id }, { preserveScroll: true })"
        >
          {{ f.label }}
        </Button>
      </template>
    </PageHeader>

    <DataTable
      v-if="rows.length" row-key="id" :rows="rows"
      :columns="[
        { key: 'review', label: 'Review', primary: true, class: 'max-w-md' },
        { key: 'author', label: 'Author' },
        { key: 'status', label: 'Status' },
        { key: 'action', label: 'Action', align: 'right' as const, hideLabel: true, full: true },
      ]"
    >
      <template #cell-review="{ row: r }">
        <span class="flex flex-wrap items-center gap-x-3 gap-y-1">
          <RatingStars :rating="r.rating" size="sm" />
          <span class="text-sm font-semibold">{{ r.product }}</span>
        </span>
        <span v-if="r.title" class="mt-1.5 block text-sm font-medium">{{ r.title }}</span>
        <span class="text-muted-foreground mt-1 block text-sm leading-6 font-normal">{{ r.body }}</span>
        <span class="text-muted-foreground tabular-nums mt-1 block text-[11px] font-normal">/products/{{ r.slug }}</span>
      </template>
      <template #cell-author="{ row: r }">
        <span class="block text-sm">{{ r.author }}</span>
        <span class="text-muted-foreground block text-xs">{{ fmtDate(r.createdAt, true) }}</span>
      </template>
      <template #cell-status="{ row: r }"><StatusBadge :status="r.status" kind="review" /></template>
      <template #cell-action="{ row: r }">
        <Button size="xs" :variant="r.status === 'published' ? 'outline' : 'default'" class="rounded-full" @click="toggle(r)">
          <EyeOff v-if="r.status === 'published'" class="size-3.5" />
          <Undo2 v-else class="size-3.5" />
          {{ r.status === 'published' ? 'Hide from the shop' : 'Unhide' }}
        </Button>
      </template>
    </DataTable>
    <EmptyState v-else :icon="Star" title="No reviews in this filter" description="Purchased devices show the stars here; hidden reviews still live but keep shop pages clean." />
  </div>
</template>
