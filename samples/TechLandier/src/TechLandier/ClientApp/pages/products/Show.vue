<script setup lang="ts">
import { Deferred, Head, Link, useForm } from '@inertiajs/vue3'
import { computed, ref } from 'vue'
import {
  BadgePercent, CreditCard, MessageSquarePlus, Package, ShieldCheck, ShoppingCart, Star, Truck,
} from '@lucide/vue'
import ProductArt from '@/components/ProductArt.vue'
import ProductCard from '@/components/ProductCard.vue'
import PriceTag from '@/components/PriceTag.vue'
import RatingStars from '@/components/RatingStars.vue'
import ReviewCard from '@/components/ReviewCard.vue'
import SpecTable from '@/components/SpecTable.vue'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { Skeleton } from '@/components/ui/skeleton'
import { Textarea } from '@/components/ui/textarea'
import { Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle } from '@/components/ui/dialog'
import { useViewBeacon } from '@/composables/useViewBeacon'
import { money } from '@/lib/money'
import type { ProductCard as ProductCardData, ProductDetail as ProductDetailData, ReviewItem } from '@/types'

const props = defineProps<{
  product: ProductDetailData
  reviews?: ReviewItem[]
  related?: ProductCardData[]
  canReview: boolean
}>()

useViewBeacon(() => props.product.id)

const inStock = computed(() => props.product.stock > 0)
const lowStock = computed(() => inStock.value && props.product.stock <= props.product.lowStockThreshold)

const bnpl = (n: number) => money(props.product.displayPrice / n)

// ---------------------------------------------------------------- review form
const reviewOpen = ref(false)
const reviewForm = useForm({ productId: props.product.id, rating: 5, title: '', body: '' })
const submitReview = () => reviewForm.post('/account/reviews', { preserveScroll: true, onSuccess: () => {
  reviewOpen.value = false
  reviewForm.reset('title', 'body')
} })
</script>

<template>
  <Head :title="product.name" />

  <div class="mx-auto max-w-7xl px-4 py-8 sm:px-6 lg:px-8">
    <!-- Breadcrumb -->
    <nav class="text-muted-foreground flex items-center gap-1.5 text-sm" aria-label="Breadcrumb">
      <Link href="/products" prefetch class="hover:text-foreground">Products</Link>
      <span aria-hidden="true">/</span>
      <Link :href="`/products?category=${product.categorySlug}`" prefetch class="hover:text-foreground">{{ product.categoryName }}</Link>
      <span aria-hidden="true">/</span>
      <span class="text-foreground truncate font-medium">{{ product.name }}</span>
    </nav>

    <div class="mt-6 grid gap-8 lg:grid-cols-[minmax(0,1fr)_26rem]">
      <!-- Art + story -->
      <div class="min-w-0 space-y-8">
        <div class="bg-card overflow-hidden rounded-3xl border shadow-xs">
          <div class="bg-muted/40 p-6 sm:p-10">
            <ProductArt
              :seed="product.slug" :icon="product.icon" :brand="product.brand" :image-url="product.imageUrl"
              :alt="product.name" rounded="rounded-2xl" class="mx-auto max-w-md"
            />
          </div>
        </div>

        <section class="bg-card rounded-3xl border p-6 shadow-xs">
          <h2 class="font-display text-lg font-semibold">About this device</h2>
          <p class="text-muted-foreground mt-3 leading-7 whitespace-pre-line">{{ product.description || product.summary }}</p>
        </section>

        <section v-if="product.specs.length" class="space-y-3">
          <h2 class="font-display text-lg font-semibold">Specs</h2>
          <SpecTable :specs="product.specs" />
        </section>

        <!-- Reviews -->
        <Deferred data="reviews">
          <template #fallback>
            <div class="space-y-3" aria-busy="true">
              <Skeleton class="h-6 w-40" />
              <Skeleton v-for="i in 2" :key="i" class="h-28 rounded-2xl" />
            </div>
          </template>
          <section class="space-y-3">
            <div class="flex flex-wrap items-center justify-between gap-3">
              <h2 class="font-display text-lg font-semibold">
                Reviews <span v-if="product.ratingCount" class="price text-muted-foreground text-sm font-normal">({{ product.ratingCount.toLocaleString() }})</span>
              </h2>
              <Button v-if="canReview" variant="outline" size="sm" class="rounded-xl" @click="reviewOpen = true">
                <MessageSquarePlus class="size-4" /> Write a review
              </Button>
            </div>
            <div v-if="reviews?.length" class="grid gap-3 sm:grid-cols-2">
              <ReviewCard v-for="review in reviews" :key="review.id" :review="review" />
            </div>
            <p v-else class="text-muted-foreground rounded-2xl border border-dashed px-5 py-8 text-center text-sm">
              No reviews yet — be the first after your delivery completes.
            </p>
          </section>
        </Deferred>
      </div>

      <!-- Buy box -->
      <aside class="lg:sticky lg:top-24 lg:self-start">
        <div class="bg-card space-y-5 rounded-3xl border p-6 shadow-xs">
          <div class="space-y-2">
            <p class="text-brand text-xs font-semibold tracking-[0.1em] uppercase">{{ product.brand }}</p>
            <h1 class="font-display text-2xl font-bold tracking-tight text-balance">{{ product.name }}</h1>
            <div class="flex flex-wrap items-center gap-3">
              <RatingStars :rating="product.ratingAvg" :count="product.ratingCount" />
              <span class="text-muted-foreground text-xs">{{ product.soldCount.toLocaleString() }} sold</span>
            </div>
            <p class="text-muted-foreground text-sm leading-6">{{ product.summary }}</p>
          </div>

          <PriceTag :price="product.displayPrice" :compare-at="product.compareAtPrice" :discount-percent="product.discountPercent" size="lg" />
          <p v-if="product.discountPercent > 0" class="text-success flex items-center gap-1.5 text-sm font-medium">
            <BadgePercent class="size-4" aria-hidden="true" /> You save {{ money((product.price - product.displayPrice)) }} ({{ product.discountPercent }}%)
          </p>

          <!-- Stock state -->
          <p v-if="inStock && !lowStock" class="border-instock/50 bg-instock/15 inline-flex items-center gap-1.5 rounded-full border px-3 py-1 text-xs font-semibold">
            <Package class="size-3.5" aria-hidden="true" /> In stock — ships today
          </p>
          <p v-else-if="lowStock" class="bg-warning/15 text-warning inline-flex items-center gap-1.5 rounded-full px-3 py-1 text-xs font-semibold">
            <Package class="size-3.5" aria-hidden="true" /> Only {{ product.stock }} left
          </p>
          <p v-else class="text-destructive bg-destructive/10 inline-flex items-center gap-1.5 rounded-full px-3 py-1 text-xs font-semibold">
            <Package class="size-3.5" aria-hidden="true" /> Out of stock
          </p>

          <!-- BNPL teaser -->
          <div v-if="inStock" class="bg-accent/50 space-y-1.5 rounded-2xl border p-4">
            <p class="flex items-center gap-2 text-sm font-semibold"><CreditCard class="text-brand size-4" aria-hidden="true" /> Pay monthly, 0% down</p>
            <p class="text-muted-foreground text-xs leading-5">
              from <span class="price text-foreground font-semibold">{{ bnpl(12) }}</span>/mo for 12 months ·
              or {{ bnpl(3) }} × 3. Approval is instant at checkout.
            </p>
          </div>

          <div class="space-y-2.5 text-sm">
            <p v-if="product.warrantyMonths > 0" class="text-muted-foreground flex items-center gap-2">
              <ShieldCheck class="text-success size-4" aria-hidden="true" /> {{ product.warrantyMonths }}-month warranty included
            </p>
            <p v-if="product.isInsurable" class="text-muted-foreground flex items-center gap-2">
              <ShieldCheck class="text-brand size-4" aria-hidden="true" /> Device insurance available at checkout
            </p>
            <p class="text-muted-foreground flex items-center gap-2">
              <Truck class="text-chart-3 size-4" aria-hidden="true" /> Zone-based delivery fees shown at checkout
            </p>
          </div>

          <Button v-if="inStock" as-child size="lg" class="glow w-full rounded-2xl font-semibold">
            <Link :href="`/buy/${product.id}`" prefetch><ShoppingCart class="size-4" /> Buy now</Link>
          </Button>
          <Button v-else size="lg" disabled class="w-full rounded-2xl font-semibold">Out of stock</Button>
        </div>
      </aside>
    </div>

    <!-- Related -->
    <Deferred data="related">
      <template #fallback><Skeleton class="mt-14 h-8 w-40" /></template>
      <section v-if="related?.length" class="mt-14">
        <h2 class="font-display text-2xl font-bold tracking-tight">Pairs well with</h2>
        <div class="mt-6 grid grid-cols-1 gap-4 sm:grid-cols-2 lg:grid-cols-3">
          <ProductCard v-for="p in related" :key="p.id" :product="p" />
        </div>
      </section>
    </Deferred>
  </div>

  <!-- Review dialog -->
  <Dialog v-model:open="reviewOpen">
    <DialogContent class="sm:max-w-md">
      <DialogHeader>
        <DialogTitle>Review {{ product.name }}</DialogTitle>
        <DialogDescription>Reviews open once your delivery completes. One review per device.</DialogDescription>
      </DialogHeader>
      <form class="space-y-4" @submit.prevent="submitReview">
        <div class="space-y-2">
          <Label>Your rating</Label>
          <RatingStars v-model:rating="reviewForm.rating" interactive size="md" />
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
            <Star v-if="!reviewForm.processing" class="size-4" aria-hidden="true" />
            {{ reviewForm.processing ? 'Publishing…' : 'Publish review' }}
          </Button>
        </DialogFooter>
      </form>
    </DialogContent>
  </Dialog>
</template>
