<script setup lang="ts">
import { computed, watch } from 'vue'
import { useForm } from '@inertiajs/vue3'
import { Save } from '@lucide/vue'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { Switch } from '@/components/ui/switch'
import { Textarea } from '@/components/ui/textarea'
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select'
import { categoryIcon } from '@/lib/icons'
import type { AdminCategoryRow, EditingProduct } from '@/types'

const props = defineProps<{ editing: EditingProduct; categories: AdminCategoryRow[] }>()

const isNew = computed(() => props.editing.id === 'new')
const specs = computed(() =>
  (props.editing.specsJson === '' ? [] : (JSON.parse(props.editing.specsJson || '[]') as { label: string; value: string }[])))

const specsText = computed(() => specs.value.map((s) => `${s.label}=${s.value}`).join('\n'))

function specsToJson(text: string): string {
  const rows = text
    .split('\n')
    .map((line) => line.trim())
    .filter(Boolean)
    .map((line) => {
      const [label, ...rest] = line.split('=')
      return { label: (label ?? '').trim(), value: rest.join('=').trim() }
    })
    .filter((s) => s.label && s.value)
  return JSON.stringify(rows)
}

const form = useForm({
  id: props.editing.id,
  name: props.editing.name,
  brand: props.editing.brand,
  categoryId: props.editing.categoryId,
  summary: props.editing.summary,
  description: props.editing.description,
  price: props.editing.price,
  discountPercent: props.editing.discountPercent,
  stock: props.editing.stock,
  lowStockThreshold: props.editing.lowStockThreshold,
  warrantyMonths: props.editing.warrantyMonths,
  isInsurable: props.editing.isInsurable,
  keywords: props.editing.keywords ?? '',
  specs: specsText.value, // "label=value" lines, serialized on submit
  status: props.editing.status,
})

// Restocks arrive as new props; keep the (disabled) stock field in step with the ledger.
watch(() => props.editing.stock, (stock) => (form.stock = stock))

const submit = () => {
  form.transform((d) => ({ ...d, specs: specsToJson(d.specs) || '[]' }))
  form.post(isNew.value ? '/admin/catalog' : `/admin/catalog/${props.editing.id}`, { preserveScroll: true })
}

// restock ledger (editor only)
const restockForm = useForm({ delta: 10, note: '' })
const restock = () => restockForm.post(`/admin/catalog/${props.editing.id}/restock`, { onSuccess: () => restockForm.reset('note') })

const status = ['draft', 'active', 'archived'] as const
</script>

<template>
  <div class="grid gap-5">
    <form class="grid gap-4" @submit.prevent="submit">
      <div class="grid gap-4 sm:grid-cols-2">
        <div class="grid gap-1.5">
          <Label for="p-name">Name</Label>
          <Input id="p-name" v-model="form.name" required minlength="3" maxlength="80" />
          <span v-if="form.errors.name" class="text-destructive text-xs">{{ form.errors.name }}</span>
        </div>
        <div class="grid gap-1.5">
          <Label for="p-brand">Brand</Label>
          <Input id="p-brand" v-model="form.brand" required maxlength="40" />
          <span v-if="form.errors.brand" class="text-destructive text-xs">{{ form.errors.brand }}</span>
        </div>
        <div class="grid gap-1.5">
          <Label for="p-cat">Category</Label>
          <Select v-model="form.categoryId">
            <SelectTrigger id="p-cat"><SelectValue placeholder="Pick a category" /></SelectTrigger>
            <SelectContent>
              <SelectItem v-for="c in categories" :key="c.id" :value="c.id">
                <span class="inline-flex items-center gap-2"><component :is="categoryIcon(c.icon)" class="size-3.5" /> {{ c.name }}</span>
              </SelectItem>
            </SelectContent>
          </Select>
          <span v-if="form.errors.categoryId" class="text-destructive text-xs">{{ form.errors.categoryId }}</span>
        </div>
        <div class="grid gap-1.5">
          <Label for="p-status">Status</Label>
          <Select v-model="form.status">
            <SelectTrigger id="p-status"><SelectValue /></SelectTrigger>
            <SelectContent>
              <SelectItem v-for="s in status" :key="s" :value="s">{{ s[0].toUpperCase() + s.slice(1) }}</SelectItem>
            </SelectContent>
          </Select>
          <p class="text-muted-foreground text-xs">Only `active` products appear in the storefront.</p>
        </div>
        <div class="grid gap-1.5">
          <Label for="p-price">Price ($)</Label>
          <Input id="p-price" v-model="form.price" type="number" min="0" step="0.01" required />
          <span v-if="form.errors.price" class="text-destructive text-xs">{{ form.errors.price }}</span>
        </div>
        <div class="grid gap-1.5">
          <Label for="p-discount">Discount % (0–70, deal lever)</Label>
          <Input id="p-discount" v-model="form.discountPercent" type="number" min="0" max="70" />
        </div>
        <div class="grid gap-1.5">
          <Label for="p-stock">Stock on hand</Label>
          <Input id="p-stock" v-model="form.stock" type="number" min="0" :disabled="!isNew" />
          <p class="text-muted-foreground text-xs">{{ isNew ? 'Opening inventory writes a restock movement.' : 'Stock lives on the ledger — use Restock below.' }}</p>
        </div>
        <div class="grid gap-1.5">
          <Label for="p-threshold">Low stock threshold</Label>
          <Input id="p-threshold" v-model="form.lowStockThreshold" type="number" min="0" />
        </div>
        <div class="grid gap-1.5">
          <Label for="p-warranty">Warranty months</Label>
          <Input id="p-warranty" v-model="form.warrantyMonths" type="number" min="0" max="72" />
        </div>
        <div class="grid gap-1.5 self-end pb-1">
          <label class="flex items-center justify-between gap-4">
            <span class="text-sm font-medium">Insurable (device cover offer)</span>
            <Switch :model-value="form.isInsurable" @update:model-value="(v: boolean) => (form.isInsurable = v)" />
          </label>
        </div>
      </div>

      <div class="grid gap-1.5">
        <Label for="p-summary">Summary (cards & meta)</Label>
        <Textarea id="p-summary" v-model="form.summary" rows="2" maxlength="280" />
        <span v-if="form.errors.summary" class="text-destructive text-xs">{{ form.errors.summary }}</span>
      </div>
      <div class="grid gap-1.5">
        <Label for="p-desc">Description</Label>
        <Textarea id="p-desc" v-model="form.description" rows="4" maxlength="4000" />
      </div>
      <div class="grid gap-1.5">
        <Label for="p-keywords">Search keywords (optional)</Label>
        <Input id="p-keywords" v-model="form.keywords" maxlength="200" placeholder="noise cancelling, over ear, bluetooth…" />
      </div>
      <div class="grid gap-1.5">
        <Label for="p-specs">Specs — one per line, `Label=Value`</Label>
        <Textarea id="p-specs" v-model="form.specs" rows="4" placeholder="Ports=USB-C x2&#10;Battery=8 000 mAh" />
        <span v-if="form.errors.specs" class="text-destructive text-xs">{{ form.errors.specs }}</span>
      </div>

      <div class="flex items-center gap-2">
        <Button type="submit" :disabled="form.processing" class="w-fit">
          <Save class="size-4" /> {{ isNew ? 'Create product' : 'Save changes' }}
        </Button>
        <span class="text-muted-foreground text-xs">Slug is derived from the name; changing it later breaks old links, so name with care.</span>
      </div>
    </form>

    <!-- restock ledger -->
    <form v-if="!isNew" class="border-border/50 flex flex-wrap items-end gap-2 rounded-xl border p-4" @submit.prevent="restock">
      <div class="w-28">
        <Label for="p-restock">Restock Δ</Label>
        <Input id="p-restock" v-model="restockForm.delta" type="number" class="mt-1" />
      </div>
      <div class="min-w-40 flex-1">
        <Label for="p-note">Ledger note</Label>
        <Input id="p-note" v-model="restockForm.note" class="mt-1" maxlength="120" placeholder="Delivery #4416 received, 6 units" />
      </div>
      <Button type="submit" size="sm" variant="outline" :disabled="restockForm.processing">Adjust stock</Button>
      <p class="text-muted-foreground w-full text-xs">
        Every adjustment writes a StockMovement — stock always equals the sum of the ledger, and low-stock alerts fire once per crossing.
      </p>
    </form>
  </div>
</template>
