<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import { Head, Link, useForm } from '@inertiajs/vue3'
import {
  ArrowLeft, Boxes, ExternalLink, Loader2, Minus, PackageMinus, PackagePlus, Pencil, Plus, ShieldCheck, ShoppingBag, Star,
} from '@lucide/vue'
import PageHeader from '@/components/PageHeader.vue'
import StatusBadge from '@/components/StatusBadge.vue'
import StatCard from '@/components/StatCard.vue'
import ProductArt from '@/components/ProductArt.vue'
import RelativeTime from '@/components/RelativeTime.vue'
import FieldError from '@/components/FieldError.vue'
import ProductSheet from '@/components/admin/ProductSheet.vue'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle } from '@/components/ui/dialog'
import { money } from '@/lib/money'
import { fmtDate, fmtDateTime } from '@/lib/time'
import type { AdminCategoryRow, AdminProductDetail, StockMovementRow } from '@/types'

const props = defineProps<{ detail: AdminProductDetail; categories: AdminCategoryRow[]; movements: StockMovementRow[] }>()

const p = computed(() => props.detail.product)
const specs = computed(() => JSON.parse(p.value.specsJson || '[]') as { label: string; value: string }[])
const salePrice = computed(() => Math.round(p.value.price * (100 - p.value.discountPercent)) / 100)
const low = computed(() => p.value.stock <= p.value.lowStockThreshold)

// Gallery: the selected photo, reset when the photos change after an edit.
const selected = ref(0)
watch(() => p.value.images.map((i) => i.assetId).join(), () => (selected.value = 0))
const shown = computed(() => p.value.images[selected.value] ?? null)

const editing = ref(false)

// Stock adjustments go through the ledger; negative numbers write stock off.
const adjusting = ref(false)
const stockForm = useForm({ delta: 10, note: '' })
watch(adjusting, (isOpen) => isOpen && (stockForm.reset(), stockForm.clearErrors()))
const after = computed(() => p.value.stock + Number(stockForm.delta || 0))
function adjust() {
  stockForm.post(`/admin/catalog/${p.value.id}/restock`, { preserveScroll: true, onSuccess: () => (adjusting.value = false) })
}
const reasonLabel = (r: string) => ({ restock: 'Restock', sale: 'Sale', release: 'Released', adjustment: 'Adjustment', damage: 'Write-off' } as Record<string, string>)[r] ?? r
</script>

<template>
  <Head :title="p.name" />

  <div class="flex flex-col gap-6">
    <Link href="/admin/catalog" class="text-muted-foreground hover:text-foreground flex w-fit items-center gap-1.5 text-sm"><ArrowLeft class="size-4" aria-hidden="true" /> Catalog</Link>

    <PageHeader :title="p.name">
      <template #description>
        <span class="flex flex-wrap items-center gap-2">{{ p.brand }} · {{ detail.category }} <StatusBadge :status="p.status" kind="product" /></span>
      </template>
      <template #actions>
        <Button v-if="p.status === 'active'" variant="ghost" as-child><a :href="`/products/${detail.slug}`" target="_blank" rel="noopener"><ExternalLink /> View in store</a></Button>
        <Button variant="outline" @click="adjusting = true"><Boxes /> Adjust stock</Button>
        <Button @click="editing = true"><Pencil /> Edit</Button>
      </template>
    </PageHeader>

    <div class="grid gap-6 lg:grid-cols-[minmax(0,1fr)_22rem]">
      <div class="flex min-w-0 flex-col gap-6">
        <!-- gallery -->
        <section class="surface overflow-hidden" aria-label="Photos">
          <div class="bg-muted relative aspect-[4/3]">
            <Transition mode="out-in" enter-active-class="transition duration-200" enter-from-class="opacity-0" leave-active-class="transition duration-100" leave-to-class="opacity-0">
              <img v-if="shown" :key="shown.assetId" :src="shown.url" :alt="`${p.name}, photo ${selected + 1}`" class="size-full object-contain" />
              <ProductArt v-else :seed="detail.slug" :icon="detail.categoryIcon" :brand="p.brand" rounded="rounded-none" class="aspect-[4/3]" />
            </Transition>
            <button v-if="!p.images.length" type="button" class="bg-background/90 hover:bg-background absolute right-3 bottom-3 rounded-full px-3 py-1.5 text-xs font-medium shadow-sm" @click="editing = true">Add photos</button>
          </div>
          <div v-if="p.images.length > 1" class="flex gap-2 overflow-x-auto border-t p-3" role="group" aria-label="Choose photo">
            <button
              v-for="(img, i) in p.images" :key="img.assetId" type="button" :aria-pressed="i === selected" :aria-label="`Show photo ${i + 1}`"
              class="size-16 shrink-0 overflow-hidden rounded-lg border-2 transition-all" :class="i === selected ? 'border-primary' : 'border-transparent opacity-70 hover:opacity-100'"
              @click="selected = i"
            >
              <img :src="img.url" alt="" class="size-full object-cover" loading="lazy" />
            </button>
          </div>
        </section>

        <section class="grid grid-cols-2 gap-3 xl:grid-cols-4" aria-label="Key numbers">
          <StatCard label="Customers pay" :value="money(salePrice)" :icon="ShoppingBag" :hint="p.discountPercent ? `${money(p.price)} less ${p.discountPercent}%` : 'No discount'" />
          <StatCard label="In stock" :value="p.stock" :icon="Boxes" :tone="low ? 'warning' : 'default'" :hint="low ? `At or below ${p.lowStockThreshold}` : `Alert at ${p.lowStockThreshold}`" />
          <StatCard label="Sold" :value="detail.soldCount" :icon="PackageMinus" hint="All time" />
          <StatCard label="Rating" :value="detail.ratingCount ? detail.ratingAvg.toFixed(1) : '—'" :icon="Star" :hint="`${detail.ratingCount} ${detail.ratingCount === 1 ? 'review' : 'reviews'}`" />
        </section>

        <section class="surface space-y-4 p-5">
          <h2 class="font-semibold">Description</h2>
          <p v-if="p.summary" class="text-sm leading-6 font-medium">{{ p.summary }}</p>
          <p v-if="p.description" class="text-muted-foreground text-sm leading-6 whitespace-pre-line">{{ p.description }}</p>
          <p v-if="!p.summary && !p.description" class="text-muted-foreground text-sm">No description yet. <button type="button" class="text-primary hover:underline" @click="editing = true">Write one</button>.</p>
          <dl v-if="specs.length" class="divide-y rounded-xl border text-sm">
            <div v-for="s in specs" :key="s.label" class="grid grid-cols-[10rem_1fr] gap-4 px-4 py-2.5">
              <dt class="text-muted-foreground">{{ s.label }}</dt><dd>{{ s.value }}</dd>
            </div>
          </dl>
        </section>
      </div>

      <aside class="flex flex-col gap-6">
        <section class="surface p-5">
          <h2 class="font-semibold">Details</h2>
          <dl class="mt-3 divide-y text-sm">
            <div class="flex justify-between gap-4 py-2"><dt class="text-muted-foreground">Category</dt><dd>{{ detail.category }}</dd></div>
            <div class="flex justify-between gap-4 py-2"><dt class="text-muted-foreground">Warranty</dt><dd>{{ p.warrantyMonths ? `${p.warrantyMonths} months` : 'None' }}</dd></div>
            <div class="flex justify-between gap-4 py-2"><dt class="text-muted-foreground">Insurance</dt><dd class="flex items-center gap-1.5"><ShieldCheck v-if="p.isInsurable" class="text-success size-4" aria-hidden="true" />{{ p.isInsurable ? 'Offered' : 'Not offered' }}</dd></div>
            <div class="flex justify-between gap-4 py-2"><dt class="text-muted-foreground">Store link</dt><dd class="truncate font-mono text-xs">/products/{{ detail.slug }}</dd></div>
            <div v-if="p.keywords" class="flex justify-between gap-4 py-2"><dt class="text-muted-foreground">Keywords</dt><dd class="text-right">{{ p.keywords }}</dd></div>
            <div class="flex justify-between gap-4 py-2"><dt class="text-muted-foreground">Created</dt><dd>{{ fmtDate(detail.createdAt, true) }}</dd></div>
            <div class="flex justify-between gap-4 py-2"><dt class="text-muted-foreground">Last change</dt><dd><RelativeTime :iso="detail.updatedAt" /></dd></div>
          </dl>
        </section>

        <section class="surface overflow-hidden">
          <header class="flex items-center justify-between border-b px-5 py-3">
            <h2 class="font-semibold">Stock history</h2>
            <span class="text-muted-foreground text-xs">Latest {{ movements.length }}</span>
          </header>
          <TransitionGroup v-if="movements.length" tag="ul" class="divide-y" enter-active-class="transition duration-300" enter-from-class="opacity-0 -translate-y-1">
            <li v-for="m in movements" :key="m.id" class="flex items-start gap-3 px-5 py-3 text-sm">
              <span class="mt-0.5 flex size-7 shrink-0 items-center justify-center rounded-full" :class="m.delta > 0 ? 'bg-success/12 text-success' : 'bg-muted text-muted-foreground'">
                <component :is="m.delta > 0 ? PackagePlus : PackageMinus" class="size-3.5" aria-hidden="true" />
              </span>
              <div class="min-w-0 flex-1">
                <p class="flex justify-between gap-2">
                  <span class="font-medium">{{ reasonLabel(m.reason) }}<template v-if="m.orderNumber"> · {{ m.orderNumber }}</template></span>
                  <span class="price shrink-0 font-semibold" :class="m.delta > 0 ? 'text-success' : ''">{{ m.delta > 0 ? '+' : '' }}{{ m.delta }}</span>
                </p>
                <p class="text-muted-foreground truncate text-xs">{{ m.note ?? '' }}{{ m.note && m.by ? ' · ' : '' }}{{ m.by ?? '' }}</p>
                <p class="text-muted-foreground text-xs" :title="fmtDateTime(m.at)"><RelativeTime :iso="m.at" /></p>
              </div>
            </li>
          </TransitionGroup>
          <p v-else class="text-muted-foreground px-5 py-8 text-center text-sm">No stock movements yet.</p>
        </section>
      </aside>
    </div>
  </div>

  <ProductSheet v-model:open="editing" :product="p" :categories="categories" />

  <Dialog v-model:open="adjusting">
    <DialogContent class="sm:max-w-sm">
      <DialogHeader>
        <DialogTitle>Adjust stock</DialogTitle>
        <DialogDescription>Each change is written to the stock history. Use a negative number to write units off.</DialogDescription>
      </DialogHeader>
      <form id="stock-form" class="space-y-4" novalidate @submit.prevent="adjust">
        <div class="space-y-2">
          <Label for="stock-delta">Change by</Label>
          <div class="flex items-center gap-2">
            <Button type="button" size="icon" variant="outline" aria-label="Decrease" @click="stockForm.delta = Number(stockForm.delta) - 1"><Minus /></Button>
            <Input id="stock-delta" v-model.number="stockForm.delta" type="number" inputmode="numeric" class="text-center tabular-nums" />
            <Button type="button" size="icon" variant="outline" aria-label="Increase" @click="stockForm.delta = Number(stockForm.delta) + 1"><Plus /></Button>
          </div>
          <div class="flex flex-wrap gap-1.5">
            <button v-for="n in [5, 10, 25, 50]" :key="n" type="button" class="bg-muted hover:bg-accent rounded-full px-2.5 py-1 text-xs transition-colors active:scale-95" @click="stockForm.delta = n">+{{ n }}</button>
          </div>
          <p class="text-sm" aria-live="polite">{{ p.stock }} → <strong class="tabular-nums" :class="after < 0 ? 'text-destructive' : after <= p.lowStockThreshold ? 'text-warning' : ''">{{ after }}</strong> in stock</p>
          <FieldError :message="stockForm.errors.delta" />
        </div>
        <div class="space-y-2">
          <Label for="stock-note">Note <span class="text-muted-foreground font-normal">(optional)</span></Label>
          <Input id="stock-note" v-model="stockForm.note" maxlength="120" placeholder="Delivery #4416, 10 units" />
        </div>
      </form>
      <DialogFooter>
        <Button type="button" variant="ghost" @click="adjusting = false">Cancel</Button>
        <Button type="submit" form="stock-form" :disabled="stockForm.processing || !Number(stockForm.delta) || after < 0">
          <Loader2 v-if="stockForm.processing" class="animate-spin" aria-hidden="true" /> Save
        </Button>
      </DialogFooter>
    </DialogContent>
  </Dialog>
</template>
