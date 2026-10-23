<script setup lang="ts">
import { computed, ref } from 'vue'
import { Deferred, Head, Link, useForm } from '@inertiajs/vue3'
import { MessageSquarePlus, Star } from '@lucide/vue'
import BuyBox from '@/components/BuyBox.vue'
import ProductArt from '@/components/ProductArt.vue'
import ProductCard from '@/components/ProductCard.vue'
import Rail from '@/components/Rail.vue'
import RatingStars from '@/components/RatingStars.vue'
import ReviewCard from '@/components/ReviewCard.vue'
import SpecTable from '@/components/SpecTable.vue'
import { Button } from '@/components/ui/button'
import { Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle } from '@/components/ui/dialog'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { Skeleton } from '@/components/ui/skeleton'
import { Textarea } from '@/components/ui/textarea'
import { useViewBeacon } from '@/composables/useViewBeacon'
import type { ProductCard as ProductCardData, ProductDetail as ProductDetailData, ReviewItem } from '@/types'

const props = defineProps<{
  product: ProductDetailData
  reviews?: ReviewItem[]
  related?: ProductCardData[]
  canReview?: boolean
}>()

useViewBeacon(() => props.product.id)

/** The first few specs double as the at-a-glance strip under the photo. */
const highlights = computed(() => props.product.specs.slice(0, 4))

// ---------------------------------------------------------------- review form
const reviewOpen = ref(false)
const reviewForm = useForm({ productId: props.product.id, rating: 5, title: '', body: '' })
const submitReview = () =>
  reviewForm.post('/account/reviews', {
    preserveScroll: true,
    onSuccess: () => {
      reviewOpen.value = false
      reviewForm.reset('title', 'body')
    },
  })
</script>

<template>
  <Head :title="product.name" />

  <div class="mx-auto max-w-7xl px-4 py-6 sm:px-6 lg:px-8">
    <nav class="text-muted-foreground flex flex-wrap items-center gap-1.5 text-sm" aria-label="Breadcrumb">
      <Link href="/products" prefetch class="hover:text-foreground">Products</Link>
      <span aria-hidden="true">/</span>
      <Link :href="`/products?category=${product.categorySlug}`" prefetch class="hover:text-foreground">{{ product.categoryName }}</Link>
    </nav>

    <div class="mt-5 grid gap-x-12 gap-y-10 lg:grid-cols-[minmax(0,1fr)_25rem]">
      <div class="bg-card overflow-hidden rounded-xl lg:col-start-1">
        <ProductArt :seed="product.slug" :icon="product.icon" :brand="product.brand" :image-url="product.imageUrl" :alt="product.name" rounded="rounded-none" class="mx-auto max-h-[34rem]" />
      </div>

      <aside class="lg:col-start-2 lg:row-span-2 lg:row-start-1">
        <div class="lg:sticky lg:top-24"><BuyBox :key="product.id" :product="product" /></div>
      </aside>

      <div class="min-w-0 space-y-12 lg:col-start-1">
        <dl v-if="highlights.length" class="grid grid-cols-2 gap-x-6 gap-y-5 sm:grid-cols-4">
          <div v-for="h in highlights" :key="h.label">
            <dt class="text-muted-foreground text-xs">{{ h.label }}</dt>
            <dd class="mt-0.5 text-sm font-medium">{{ h.value }}</dd>
          </div>
        </dl>

        <section v-if="product.description || product.summary" aria-labelledby="about-h">
          <h2 id="about-h" class="text-xl font-semibold">About this device</h2>
          <p class="text-muted-foreground mt-3 max-w-prose leading-7 whitespace-pre-line">{{ product.description || product.summary }}</p>
        </section>

        <section v-if="product.specs.length" aria-labelledby="specs-h">
          <h2 id="specs-h" class="text-xl font-semibold">Specifications</h2>
          <SpecTable :specs="product.specs" class="mt-3" />
        </section>

        <section aria-labelledby="reviews-h">
          <div class="flex flex-wrap items-center justify-between gap-3">
            <h2 id="reviews-h" class="text-xl font-semibold">Reviews</h2>
            <Button v-if="canReview" variant="outline" size="sm" @click="reviewOpen = true"><MessageSquarePlus /> Write a review</Button>
          </div>
          <div v-if="product.ratingCount > 0" class="mt-4 flex items-center gap-4">
            <span class="price text-5xl font-semibold tracking-tight">{{ product.ratingAvg.toFixed(1) }}</span>
            <div class="space-y-1">
              <RatingStars :rating="product.ratingAvg" />
              <p class="text-muted-foreground text-sm">{{ product.ratingCount.toLocaleString() }} verified {{ product.ratingCount === 1 ? 'review' : 'reviews' }}</p>
            </div>
          </div>
          <Deferred data="reviews">
            <template #fallback>
              <div class="mt-5 grid gap-3 sm:grid-cols-2" aria-busy="true">
                <Skeleton v-for="i in 2" :key="i" class="h-32 rounded-xl" />
              </div>
            </template>
            <div v-if="reviews?.length" class="mt-5 grid gap-3 sm:grid-cols-2">
              <ReviewCard v-for="review in reviews" :key="review.id" :review="review" />
            </div>
            <p v-else class="text-muted-foreground mt-4 text-sm">No reviews yet. Only customers who received this product can review it.</p>
          </Deferred>
        </section>
      </div>
    </div>

    <Deferred data="related">
      <template #fallback>
        <div class="mt-16 flex gap-4 overflow-hidden"><Skeleton v-for="i in 4" :key="i" class="aspect-[4/5] w-56 shrink-0 rounded-xl" /></div>
      </template>
      <Rail v-if="related?.length" title="You might also like" class="!px-0">
        <ProductCard v-for="p in related" :key="p.id" :product="p" class="w-56 shrink-0 snap-start sm:w-60" />
      </Rail>
    </Deferred>
  </div>

  <Dialog v-model:open="reviewOpen">
    <DialogContent class="sm:max-w-md">
      <DialogHeader>
        <DialogTitle>Review {{ product.name }}</DialogTitle>
        <DialogDescription>One review per device, from customers whose order was delivered.</DialogDescription>
      </DialogHeader>
      <form class="space-y-4" @submit.prevent="submitReview">
        <div class="space-y-2">
          <Label>Your rating</Label>
          <RatingStars v-model:rating="reviewForm.rating" :count="null" interactive size="md" />
          <p v-if="reviewForm.errors.rating" role="alert" class="text-destructive text-sm">{{ reviewForm.errors.rating }}</p>
        </div>
        <div class="space-y-2">
          <Label for="review-title">Headline <span class="text-muted-foreground font-normal">(optional)</span></Label>
          <Input id="review-title" v-model="reviewForm.title" maxlength="80" />
        </div>
        <div class="space-y-2">
          <Label for="review-body">Your review</Label>
          <Textarea id="review-body" v-model="reviewForm.body" rows="4" required :aria-invalid="!!reviewForm.errors.body" />
          <p v-if="reviewForm.errors.body" role="alert" class="text-destructive text-sm">{{ reviewForm.errors.body }}</p>
        </div>
        <DialogFooter>
          <Button type="button" variant="ghost" @click="reviewOpen = false">Cancel</Button>
          <Button type="submit" :disabled="reviewForm.processing">
            <Star v-if="!reviewForm.processing" aria-hidden="true" />
            {{ reviewForm.processing ? 'Publishing…' : 'Publish review' }}
          </Button>
        </DialogFooter>
      </form>
    </DialogContent>
  </Dialog>
</template>
