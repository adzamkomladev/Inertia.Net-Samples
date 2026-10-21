<script setup lang="ts">
import { computed } from 'vue'
import { Head, router } from '@inertiajs/vue3'
import { EyeOff, Star, Undo2 } from '@lucide/vue'
import PageHeader from '@/components/PageHeader.vue'
import StatusBadge from '@/components/StatusBadge.vue'
import EmptyState from '@/components/EmptyState.vue'
import RatingStars from '@/components/RatingStars.vue'
import { Button } from '@/components/ui/button'
import { fmtDate } from '@/lib/time'
import type { AdminReviewRow } from '@/types'

const props = defineProps<{ reviews: { data: AdminReviewRow[]; page: number; hasMore: boolean }; state: string }>()
const rows = computed(() => props.reviews?.data ?? [])

const filters = [
  { id: 'published', label: 'Published' },
  { id: 'hidden', label: 'Hidden' },
]
const toggle = (r: AdminReviewRow) =>
  router.post(`/admin/reviews/${r.id}`, { state: r.status === 'published' ? 'hidden' : 'published' }, { preserveScroll: true })
</script>

<template>
  <Head title="Reviews" />
    <Head title="Reviews" />

  <div class="flex flex-col gap-6">
    <PageHeader title="Reviews" description="Verified purchases only — moderate by hiding X-rated nonsense or salvaging a buried 5-star.">
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

    <div v-if="rows.length" class="flex flex-col gap-3">
      <div v-for="r in rows" :key="r.id" class="bg-card shadow-card rounded-2xl border p-4">
        <div class="flex flex-wrap items-center gap-x-3 gap-y-1.5">
          <RatingStars :rating="r.rating" size="sm" />
          <span class="text-sm font-semibold">{{ r.product }}</span>
          <StatusBadge :status="r.status" kind="review" size="xs" />
          <span class="text-muted-foreground ml-auto text-xs">{{ r.author }} · {{ fmtDate(r.createdAt, true) }}</span>
        </div>
        <p class="text-muted-foreground font-mono mt-0.5 text-[11px]">/products/{{ r.slug }}</p>
        <p v-if="r.title" class="mt-2 text-sm font-medium">{{ r.title }}</p>
        <p class="text-muted-foreground mt-1 text-sm leading-6">{{ r.body }}</p>
        <div class="mt-3">
          <Button
            size="xs"
            :variant="r.status === 'published' ? 'outline' : 'default'"
            class="rounded-full"
            @click="toggle(r)"
          >
            <EyeOff v-if="r.status === 'published'" class="size-3.5" />
            <Undo2 v-else class="size-3.5" />
            {{ r.status === 'published' ? 'Hide from the shop' : 'Unhide' }}
          </Button>
        </div>
      </div>
    </div>

    <EmptyState v-else :icon="Star" title="No reviews in this filter" description="Purchased devices show the stars here; hidden reviews still live but keep shop pages clean." />
  </div>
</template>
