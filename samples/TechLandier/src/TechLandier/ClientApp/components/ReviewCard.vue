<script setup lang="ts">
import { BadgeCheck } from '@lucide/vue'
import RatingStars from '@/components/RatingStars.vue'
import RelativeTime from '@/components/RelativeTime.vue'
import type { ReviewItem } from '@/types'

const props = defineProps<{ review: ReviewItem }>()
</script>

<!-- A published product review. -->
<template>
  <article class="bg-card space-y-2 rounded-2xl border p-4 shadow-xs">
    <div class="flex flex-wrap items-center justify-between gap-2">
      <div class="flex items-center gap-2.5">
        <span class="bg-accent text-accent-foreground flex size-8 items-center justify-center rounded-full text-xs font-semibold" aria-hidden="true">
          {{ review.authorName.slice(0, 1).toUpperCase() }}
        </span>
        <div>
          <p class="flex items-center gap-1 text-sm font-medium">
            {{ review.authorName }}
            <BadgeCheck v-if="review.verifiedPurchase" class="text-success size-3.5" aria-label="Verified purchase" />
          </p>
          <p class="text-muted-foreground text-xs"><RelativeTime :iso="review.createdAt" /></p>
        </div>
      </div>
      <RatingStars :rating="review.rating" size="sm" />
    </div>
    <p v-if="review.title" class="text-sm font-medium">{{ review.title }}</p>
    <p class="text-muted-foreground text-sm leading-6">{{ review.body }}</p>
  </article>
</template>
