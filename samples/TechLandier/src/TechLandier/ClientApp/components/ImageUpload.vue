<script setup lang="ts">
import { computed, onBeforeUnmount, ref, useId, watch } from 'vue'
import { useDropZone } from '@vueuse/core'
import { ImagePlus, Loader2, X } from '@lucide/vue'
import { Button } from '@/components/ui/button'
import { cn } from '@/lib/utils'
import { useUpload } from '@/composables/useUpload'
import type { UploadedAsset } from '@/types'

const props = defineProps<{ modelValue: string | null; previewUrl?: string | null; label?: string; previewClass?: string }>()
const emit = defineEmits<{ 'update:modelValue': [id: string | null]; uploaded: [asset: UploadedAsset]; uploading: [value: boolean] }>()

const { upload, progress, uploading, error } = useUpload()
watch(uploading, value => emit('uploading', value), { flush: 'sync' })
const inputId = useId()
const input = ref<HTMLInputElement | null>(null)
const zone = ref<HTMLElement | null>(null)
const localUrl = ref<string | null>(null) // object URL while uploading
const uploadedUrl = ref<string | null>(null)
const removed = ref(false)

const shown = computed(() => localUrl.value ?? uploadedUrl.value ?? (removed.value || props.modelValue === null ? null : props.previewUrl ?? null))

function revoke() {
  if (localUrl.value) URL.revokeObjectURL(localUrl.value)
  localUrl.value = null
}

async function handle(file: File | undefined) {
  if (!file || uploading.value) return
  revoke()
  localUrl.value = URL.createObjectURL(file)
  try {
    const asset = await upload(file)
    uploadedUrl.value = asset.url
    removed.value = false
    emit('update:modelValue', asset.id)
    emit('uploaded', asset)
  } catch {
    // message is in `error`
  } finally {
    revoke()
  }
}

function onChange(e: Event) {
  const el = e.target as HTMLInputElement
  handle(el.files?.[0])
  el.value = '' // allow re-selecting the same file
}

const { isOverDropZone: dragging } = useDropZone(zone, { onDrop: files => handle(files?.[0]), multiple: false, preventDefaultForUnhandled: true })

function remove() {
  revoke()
  uploadedUrl.value = null
  removed.value = true
  error.value = null
  emit('update:modelValue', null)
}

onBeforeUnmount(revoke)
</script>

<template>
  <div class="space-y-2">
    <label v-if="label" :for="inputId" class="text-sm font-medium leading-none">{{ label }}</label>

    <div ref="zone">
      <div v-if="shown" class="relative">
        <img :src="shown" alt="" :class="cn('bg-muted mx-auto rounded-xl border transition-opacity', previewClass ?? 'max-h-48 object-contain', uploading && 'opacity-60')" />
        <Loader2 v-if="uploading" class="absolute inset-0 m-auto size-6 animate-spin" aria-hidden="true" />
        <Button
          v-else type="button" size="icon-sm" variant="secondary" class="absolute top-2 right-2 rounded-full"
          :aria-label="`Remove ${label?.toLowerCase() ?? 'image'}`" @click="remove"
        >
          <X />
        </Button>
      </div>
      <div
        v-else
        :class="cn(
          'text-muted-foreground flex min-h-36 flex-col items-center justify-center gap-1.5 rounded-xl border border-dashed p-4 text-center text-sm transition-colors',
          dragging ? 'border-brand bg-brand/5 text-foreground' : 'border-input bg-muted/30',
        )"
      >
        <Loader2 v-if="uploading" class="size-5 animate-spin" aria-hidden="true" />
        <ImagePlus v-else class="size-5" :stroke-width="1.75" aria-hidden="true" />
        <span>{{ uploading ? 'Uploading…' : dragging ? 'Drop to upload' : 'Drag an image here' }}</span>
        <Button type="button" variant="outline" size="sm" class="mt-1 rounded-full" :disabled="uploading" @click="input?.click()">Choose image</Button>
      </div>
    </div>

    <input
      :id="inputId"
      ref="input"
      type="file"
      class="sr-only"
      :aria-label="label ? undefined : 'Image'"
      accept="image/png,image/jpeg,image/webp,image/gif"
      :disabled="uploading"
      @change="onChange"
    />

    <Button v-if="shown && !uploading" type="button" variant="outline" size="sm" class="rounded-full" @click="input?.click()">Replace image</Button>

    <div v-if="uploading" class="bg-muted h-1.5 w-full overflow-hidden rounded-full" role="progressbar" :aria-valuenow="progress" aria-valuemin="0" aria-valuemax="100">
      <div class="bg-primary h-full transition-all" :style="{ width: progress + '%' }" />
    </div>
    <p v-if="error" class="text-destructive text-sm" role="alert">{{ error }}</p>
  </div>
</template>
