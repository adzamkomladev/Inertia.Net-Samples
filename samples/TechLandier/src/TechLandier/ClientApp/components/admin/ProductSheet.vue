<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import { useForm } from '@inertiajs/vue3'
import { Loader2, Plus, X } from '@lucide/vue'
import FieldError from '@/components/FieldError.vue'
import ProductImages from '@/components/admin/ProductImages.vue'
import SegmentedControl from '@/components/SegmentedControl.vue'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { Switch } from '@/components/ui/switch'
import { Textarea } from '@/components/ui/textarea'
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select'
import { Sheet, SheetContent, SheetDescription, SheetFooter, SheetHeader, SheetTitle } from '@/components/ui/sheet'
import { categoryIcon } from '@/lib/icons'
import { money } from '@/lib/money'
import type { AdminCategoryRow, EditingProduct, ProductImageRow } from '@/types'

/** `product` null = create. */
const props = defineProps<{ product: EditingProduct | null; categories: AdminCategoryRow[] }>()
const open = defineModel<boolean>('open', { required: true })

type Spec = { label: string; value: string }
const WARRANTY = [0, 12, 24, 36]

function fromProduct(p: EditingProduct | null) {
  return {
    name: p?.name ?? '',
    brand: p?.brand ?? '',
    categoryId: p?.categoryId ?? props.categories[0]?.id ?? '',
    status: p?.status ?? 'draft',
    price: p?.price ?? 0,
    discountPercent: p?.discountPercent ?? 0,
    stock: p?.stock ?? 0,
    lowStockThreshold: p?.lowStockThreshold ?? 5,
    warrantyMonths: p?.warrantyMonths ?? 12,
    isInsurable: p?.isInsurable ?? false,
    summary: p?.summary ?? '',
    description: p?.description ?? '',
    keywords: p?.keywords ?? '',
    specs: JSON.parse(p?.specsJson || '[]') as Spec[],
    images: [...(p?.images ?? [])] as ProductImageRow[],
  }
}

const form = useForm(fromProduct(props.product))
const uploading = ref(false)

// Each opening starts from the product as it is now (or a blank one); stale errors go.
watch(open, (isOpen) => {
  if (!isOpen) return
  uploading.value = false
  form.defaults(fromProduct(props.product))
  form.reset()
  form.clearErrors()
})

const isNew = computed(() => props.product === null)
const salePrice = computed(() => Math.round(Number(form.price || 0) * (100 - Number(form.discountPercent || 0))) / 100)

const addSpec = () => form.specs.push({ label: '', value: '' })
const removeSpec = (i: number) => form.specs.splice(i, 1)

function submit() {
  form
    .transform((d) => ({
      ...d,
      specs: JSON.stringify(d.specs.map((s) => ({ label: s.label.trim(), value: s.value.trim() })).filter((s) => s.label && s.value)),
      imageAssetIds: d.images.map((i) => i.assetId),
      images: undefined,
      stock: isNew.value ? d.stock : undefined, // after creation stock only moves through the ledger
    }))
    .post(isNew.value ? '/admin/catalog' : `/admin/catalog/${props.product!.id}`, {
      preserveScroll: true,
      onSuccess: () => (open.value = false),
    })
}
</script>

<template>
  <Sheet v-model:open="open">
    <SheetContent class="w-full gap-0 sm:max-w-2xl">
      <SheetHeader class="border-b">
        <SheetTitle>{{ isNew ? 'New product' : `Edit ${product!.name}` }}</SheetTitle>
        <SheetDescription>{{ isNew ? 'Drafts stay hidden from the store until you set them active.' : 'Changes go live as soon as you save.' }}</SheetDescription>
      </SheetHeader>

      <form id="product-form" class="flex-1 space-y-8 overflow-y-auto p-4 sm:p-6" novalidate @submit.prevent="submit">
        <section class="space-y-3">
          <h3 class="text-sm font-semibold">Photos</h3>
          <ProductImages v-model="form.images" v-model:busy="uploading" />
          <FieldError :message="form.errors.images" />
        </section>

        <section class="space-y-4">
          <h3 class="text-sm font-semibold">Basics</h3>
          <div class="grid gap-4 sm:grid-cols-2">
            <div class="space-y-2 sm:col-span-2">
              <Label for="p-name">Product name</Label>
              <Input id="p-name" v-model="form.name" maxlength="80" placeholder="e.g. SonicWave Studio Pro" :aria-invalid="!!form.errors.name" />
              <FieldError :message="form.errors.name" />
            </div>
            <div class="space-y-2">
              <Label for="p-brand">Brand</Label>
              <Input id="p-brand" v-model="form.brand" maxlength="80" :aria-invalid="!!form.errors.brand" />
              <FieldError :message="form.errors.brand" />
            </div>
            <div class="space-y-2">
              <Label for="p-cat">Category</Label>
              <Select v-model="form.categoryId">
                <SelectTrigger id="p-cat" class="w-full" :aria-invalid="!!form.errors.categoryId"><SelectValue placeholder="Pick a category" /></SelectTrigger>
                <SelectContent>
                  <SelectItem v-for="c in categories" :key="c.id" :value="c.id">
                    <component :is="categoryIcon(c.icon)" class="size-4" aria-hidden="true" /> {{ c.name }}
                  </SelectItem>
                </SelectContent>
              </Select>
              <FieldError :message="form.errors.categoryId" />
            </div>
            <div class="space-y-2 sm:col-span-2">
              <Label id="p-status-label">Visibility</Label>
              <SegmentedControl
                v-model="form.status" label="Product visibility"
                :options="[{ value: 'draft', label: 'Draft' }, { value: 'active', label: 'Active' }, ...(isNew ? [] : [{ value: 'archived', label: 'Archived' }])]"
              />
              <p class="text-muted-foreground text-xs">Only active products show in the store. Archiving keeps order history intact.</p>
            </div>
          </div>
        </section>

        <section class="space-y-4">
          <h3 class="text-sm font-semibold">Pricing</h3>
          <div class="grid gap-4 sm:grid-cols-3">
            <div class="space-y-2">
              <Label for="p-price">Price</Label>
              <div class="relative">
                <span class="text-muted-foreground pointer-events-none absolute inset-y-0 left-3 flex items-center text-sm">$</span>
                <Input id="p-price" v-model.number="form.price" type="number" inputmode="decimal" min="0" step="0.01" class="pl-7" :aria-invalid="!!form.errors.price" />
              </div>
              <FieldError :message="form.errors.price" />
            </div>
            <div class="space-y-2">
              <Label for="p-discount">Discount</Label>
              <div class="relative">
                <Input id="p-discount" v-model.number="form.discountPercent" type="number" inputmode="numeric" min="0" max="70" class="pr-8" :aria-invalid="!!form.errors.discountPercent" />
                <span class="text-muted-foreground pointer-events-none absolute inset-y-0 right-3 flex items-center text-sm">%</span>
              </div>
              <FieldError :message="form.errors.discountPercent" />
            </div>
            <div class="bg-muted/60 flex flex-col justify-center rounded-xl px-4 py-2" aria-live="polite">
              <span class="text-muted-foreground text-xs">Customers pay</span>
              <span class="price text-lg font-semibold">{{ money(salePrice) }}</span>
            </div>
          </div>
        </section>

        <section class="space-y-4">
          <h3 class="text-sm font-semibold">Inventory &amp; cover</h3>
          <div class="grid gap-4 sm:grid-cols-3">
            <div v-if="isNew" class="space-y-2">
              <Label for="p-stock">Opening stock</Label>
              <Input id="p-stock" v-model.number="form.stock" type="number" inputmode="numeric" min="0" />
            </div>
            <div class="space-y-2">
              <Label for="p-threshold">Low-stock alert at</Label>
              <Input id="p-threshold" v-model.number="form.lowStockThreshold" type="number" inputmode="numeric" min="0" />
            </div>
            <div class="space-y-2">
              <Label for="p-warranty">Warranty</Label>
              <Select :model-value="String(form.warrantyMonths)" @update:model-value="(v) => (form.warrantyMonths = Number(v))">
                <SelectTrigger id="p-warranty" class="w-full"><SelectValue /></SelectTrigger>
                <SelectContent>
                  <SelectItem v-for="m in WARRANTY" :key="m" :value="String(m)">{{ m ? `${m} months` : 'No warranty' }}</SelectItem>
                </SelectContent>
              </Select>
            </div>
          </div>
          <p v-if="!isNew" class="text-muted-foreground text-xs">Stock is {{ product!.stock }}; change it with <strong>Adjust stock</strong> on the product page so the ledger stays exact.</p>
          <label for="p-insurable" class="hover:bg-muted/50 flex cursor-pointer items-start justify-between gap-4 rounded-xl border p-4 transition-colors">
            <span class="space-y-0.5">
              <span class="block text-sm font-medium">Offer device insurance</span>
              <span class="text-muted-foreground block text-xs">12 months of cover offered at checkout for 15% of the price.</span>
            </span>
            <Switch id="p-insurable" v-model="form.isInsurable" />
          </label>
        </section>

        <section class="space-y-4">
          <h3 class="text-sm font-semibold">Description</h3>
          <div class="space-y-2">
            <div class="flex justify-between"><Label for="p-summary">Summary</Label><span class="text-muted-foreground text-xs tabular-nums">{{ form.summary.length }}/280</span></div>
            <Textarea id="p-summary" v-model="form.summary" rows="2" maxlength="280" placeholder="One or two lines for cards and search results." :aria-invalid="!!form.errors.summary" />
            <FieldError :message="form.errors.summary" />
          </div>
          <div class="space-y-2">
            <Label for="p-desc">Full description</Label>
            <Textarea id="p-desc" v-model="form.description" rows="5" maxlength="4000" />
          </div>
          <div class="space-y-2">
            <Label for="p-keywords">Search keywords <span class="text-muted-foreground font-normal">(optional)</span></Label>
            <Input id="p-keywords" v-model="form.keywords" maxlength="200" placeholder="noise cancelling, over ear, bluetooth" />
          </div>
        </section>

        <section class="space-y-3">
          <div class="flex items-center justify-between">
            <h3 class="text-sm font-semibold">Specifications</h3>
            <Button type="button" size="sm" variant="ghost" @click="addSpec"><Plus /> Add row</Button>
          </div>
          <TransitionGroup tag="ul" class="space-y-2" enter-active-class="transition duration-200" enter-from-class="opacity-0 -translate-y-1" leave-active-class="transition duration-150" leave-to-class="opacity-0">
            <li v-for="(spec, i) in form.specs" :key="i" class="flex gap-2">
              <Input v-model="spec.label" maxlength="40" placeholder="Label, e.g. Battery" :aria-label="`Spec ${i + 1} label`" class="w-2/5" />
              <Input v-model="spec.value" maxlength="120" placeholder="Value, e.g. 30 hours" :aria-label="`Spec ${i + 1} value`" class="flex-1" />
              <Button type="button" size="icon-sm" variant="ghost" :aria-label="`Remove spec ${i + 1}`" @click="removeSpec(i)"><X /></Button>
            </li>
          </TransitionGroup>
          <p v-if="!form.specs.length" class="text-muted-foreground rounded-xl border border-dashed p-4 text-center text-sm">No specs yet. Rows with a label and value show as a table on the product page.</p>
        </section>
      </form>

      <SheetFooter class="flex-row items-center justify-end border-t">
        <span v-if="uploading" class="text-muted-foreground mr-auto flex items-center gap-1.5 text-xs"><Loader2 class="size-3.5 animate-spin" aria-hidden="true" /> Waiting for photos…</span>
        <Button type="button" variant="ghost" @click="open = false">Cancel</Button>
        <Button type="submit" form="product-form" :disabled="form.processing || uploading">
          <Loader2 v-if="form.processing" class="animate-spin" aria-hidden="true" />
          {{ isNew ? 'Create product' : 'Save changes' }}
        </Button>
      </SheetFooter>
    </SheetContent>
  </Sheet>
</template>
