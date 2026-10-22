<script setup lang="ts">
import { BadgeCheck } from '@lucide/vue'
import RatingStars from '@/components/RatingStars.vue'
import RelativeTime from '@/components/RelativeTime.vue'
import type { ReviewItem } from '@/types'

defineProps<{ review: ReviewItem }>()
</script>

<!-- A published product review. -->
<template>
  <article class="surface space-y-2 p-5">
    <RatingStars :rating="review.rating" size="sm" />
    <p v-if="review.title" class="text-sm font-medium">{{ review.title }}</p>
    <p class="text-muted-foreground text-sm leading-6">{{ review.body }}</p>
    <p class="text-muted-foreground flex flex-wrap items-center gap-x-1.5 pt-1 text-xs">
      <span class="text-foreground font-medium">{{ review.authorName }}</span>
      <span v-if="review.verifiedPurchase" class="text-success inline-flex items-center gap-1"><BadgeCheck class="size-3.5" aria-hidden="true" /> Verified purchase</span>
      <span aria-hidden="true">·</span>
      <RelativeTime :iso="review.createdAt" />
    </p>
  </article>
</template>
