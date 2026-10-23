<script setup lang="ts">
import { computed, onBeforeUnmount, reactive, useId, useTemplateRef } from 'vue'
import { useDropZone } from '@vueuse/core'
import { ArrowLeft, ArrowRight, ImagePlus, Loader2, Star, TriangleAlert, X } from '@lucide/vue'
import { Button } from '@/components/ui/button'
import { useUpload } from '@/composables/useUpload'
import { cn } from '@/lib/utils'
import type { ProductImageRow } from '@/types'

const MAX = 8
const TYPES = ['image/png', 'image/jpeg', 'image/webp', 'image/gif']

/** Saved gallery, in order; the first is the cover. Only finished uploads ever reach the model. */
const images = defineModel<ProductImageRow[]>({ required: true })
/** True while any file is still uploading, so the parent can hold the submit. */
const busy = defineModel<boolean>('busy', { default: false })

type Pending = { key: string; preview: string; progress: number; error: string | null }
const pending = reactive<Pending[]>([])

const inputId = useId()
const zone = useTemplateRef<HTMLElement>('zone')
const room = computed(() => MAX - images.value.length - pending.filter((p) => !p.error).length)
// Closing the sheet mid-upload unmounts this editor: late results are dropped and the parent is told it is idle.
let alive = true

function syncBusy() {
  busy.value = pending.some((p) => !p.error)
}

async function add(files: File[] | FileList | null | undefined) {
  const list = Array.from(files ?? []).filter((f) => TYPES.includes(f.type)).slice(0, Math.max(0, room.value))
  // Uploads run in parallel; each tile tracks its own progress and error.
  await Promise.all(list.map(async (file) => {
    const item = reactive<Pending>({ key: crypto.randomUUID(), preview: URL.createObjectURL(file), progress: 0, error: null })
    pending.push(item)
    syncBusy()
    const { upload, progress, error } = useUpload()
    const stop = setInterval(() => (item.progress = progress.value), 120)
    try {
      const asset = await upload(file)
      if (!alive) return
      images.value = [...images.value, { assetId: asset.id, url: asset.url }]
      pending.splice(pending.indexOf(item), 1)
      URL.revokeObjectURL(item.preview)
    } catch {
      item.error = error.value ?? 'Upload failed.'
    } finally {
      clearInterval(stop)
      if (alive) syncBusy()
    }
  }))
}

async function onPick(e: Event) {
  const el = e.target as HTMLInputElement
  const files = Array.from(el.files ?? [])
  el.value = '' // allow picking the same file again
  await add(files)
}

const { isOverDropZone: dragging } = useDropZone(zone, {
  onDrop: async (files) => await add(files), dataTypes: TYPES, multiple: true, preventDefaultForUnhandled: true,
})

function move(index: number, by: -1 | 1) {
  const next = [...images.value]
  const [item] = next.splice(index, 1)
  next.splice(index + by, 0, item!)
  images.value = next
}
const makeCover = (index: number) => {
  const next = [...images.value]
  next.unshift(next.splice(index, 1)[0]!)
  images.value = next
}
const remove = (index: number) => (images.value = images.value.filter((_, i) => i !== index))
function dismiss(item: Pending) {
  URL.revokeObjectURL(item.preview)
  pending.splice(pending.indexOf(item), 1)
}

onBeforeUnmount(() => {
  alive = false
  busy.value = false
  pending.forEach((p) => URL.revokeObjectURL(p.preview))
})
</script>

<!-- Product gallery editor: drop or pick several photos, reorder them, choose the cover. -->
<template>
  <div class="space-y-3">
    <TransitionGroup
      tag="ul" class="grid grid-cols-2 gap-2 sm:grid-cols-4" aria-label="Product images"
      move-class="transition-transform duration-300" enter-active-class="transition duration-300 ease-(--ease-out-soft)"
      enter-from-class="scale-95 opacity-0" leave-active-class="transition duration-150" leave-to-class="scale-95 opacity-0"
    >
      <li v-for="(img, i) in images" :key="img.assetId" class="group bg-muted relative aspect-square overflow-hidden rounded-xl border">
        <img :src="img.url" :alt="`Product image ${i + 1}`" class="size-full object-cover" loading="lazy" />
        <span v-if="i === 0" class="bg-foreground/80 text-background absolute top-1.5 left-1.5 flex items-center gap-1 rounded-full px-2 py-0.5 text-[10px] font-semibold"><Star class="size-3" aria-hidden="true" /> Cover</span>
        <!-- Remove sits top-right; order and cover controls share the bottom edge, so each row fits a phone-width tile. -->
        <Button type="button" size="icon-xs" variant="secondary" class="hover:text-destructive absolute top-1.5 right-1.5 shadow-sm transition-opacity sm:opacity-0 sm:group-hover:opacity-100 sm:group-focus-within:opacity-100" :aria-label="`Remove image ${i + 1}`" @click="remove(i)"><X /></Button>
        <div class="absolute inset-x-1.5 bottom-1.5 flex justify-between gap-1 transition-opacity sm:opacity-0 sm:group-hover:opacity-100 sm:group-focus-within:opacity-100">
          <div class="flex gap-1">
            <Button type="button" size="icon-xs" variant="secondary" class="shadow-sm" :disabled="i === 0" :aria-label="`Move image ${i + 1} earlier`" @click="move(i, -1)"><ArrowLeft /></Button>
            <Button type="button" size="icon-xs" variant="secondary" class="shadow-sm" :disabled="i === images.length - 1" :aria-label="`Move image ${i + 1} later`" @click="move(i, 1)"><ArrowRight /></Button>
          </div>
          <Button v-if="i > 0" type="button" size="icon-xs" variant="secondary" class="shadow-sm" :aria-label="`Use image ${i + 1} as cover`" @click="makeCover(i)"><Star /></Button>
        </div>
      </li>
      <li v-for="p in pending" :key="p.key" class="bg-muted relative aspect-square overflow-hidden rounded-xl border" :aria-busy="!p.error">
        <img :src="p.preview" alt="" class="size-full object-cover" :class="p.error ? 'opacity-30' : 'opacity-60'" />
        <div v-if="!p.error" class="absolute inset-0 flex flex-col items-center justify-center gap-2">
          <Loader2 class="size-5 animate-spin" aria-hidden="true" />
          <div class="bg-background/70 h-1 w-2/3 overflow-hidden rounded-full"><div class="bg-primary h-full transition-[width] duration-200" :style="{ width: `${p.progress}%` }" /></div>
          <span class="sr-only">Uploading, {{ p.progress }}%</span>
        </div>
        <div v-else class="absolute inset-0 flex flex-col items-center justify-center gap-1 p-2 text-center" role="alert">
          <TriangleAlert class="text-destructive size-5" aria-hidden="true" />
          <span class="text-destructive text-[11px] leading-tight">{{ p.error }}</span>
          <button type="button" class="text-xs underline" @click="dismiss(p)">Dismiss</button>
        </div>
      </li>
      <li v-if="room > 0" key="add">
        <div
          ref="zone"
          :class="cn('flex aspect-square flex-col items-center justify-center gap-1.5 rounded-xl border-2 border-dashed text-center transition-colors', dragging ? 'border-primary bg-accent text-accent-foreground' : 'text-muted-foreground hover:border-primary/50 hover:bg-muted/50')"
        >
          <label :for="inputId" class="flex size-full cursor-pointer flex-col items-center justify-center gap-1.5 rounded-xl p-2 focus-within:ring-2 focus-within:ring-ring">
            <ImagePlus class="size-5" :class="dragging && 'animate-pop'" aria-hidden="true" />
            <span class="text-xs font-medium">{{ dragging ? 'Drop to upload' : images.length ? 'Add more' : 'Add photos' }}</span>
            <span class="text-[10px]">PNG, JPG, WebP · 5 MB</span>
            <input :id="inputId" type="file" class="sr-only" :accept="TYPES.join(',')" multiple @change="onPick" />
          </label>
        </div>
      </li>
    </TransitionGroup>
    <p class="text-muted-foreground text-xs">{{ images.length }}/{{ MAX }} photos. The first is the cover on cards and search; drag files in or use the arrows to reorder.</p>
  </div>
</template>
