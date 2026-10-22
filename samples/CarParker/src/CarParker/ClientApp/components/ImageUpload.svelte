<script lang="ts">
  import ImagePlus from '@lucide/svelte/icons/image-plus'
  import Loader2 from '@lucide/svelte/icons/loader-circle'
  import X from '@lucide/svelte/icons/x'
  import Button from '@/components/ui/button/Button.svelte'
  import { useUpload } from '@/lib/upload.svelte'
  import { cn } from '@/lib/utils'
  import type { UploadedAsset } from '@/types'

  let {
    value = $bindable(),
    previewUrl,
    label,
    previewClass,
    onuploaded,
    onuploadingchange,
  }: {
    value?: string | null
    previewUrl?: string | null
    label?: string
    previewClass?: string
    onuploaded?: (asset: UploadedAsset) => void
    onuploadingchange?: (value: boolean) => void
  } = $props()

  const uploader = useUpload()
  $effect(() => onuploadingchange?.(uploader.uploading))

  const inputId = `upload-${Math.random().toString(36).slice(2, 9)}`
  let input: HTMLInputElement | undefined = $state()
  let localUrl: string | null = $state(null) // object URL while uploading
  let uploadedUrl: string | null = $state(null)
  let removed = $state(false)
  let dragging = $state(false)

  const shown = $derived(localUrl ?? uploadedUrl ?? (removed || value === null || value === undefined ? null : previewUrl ?? null))

  function revoke() {
    if (localUrl) URL.revokeObjectURL(localUrl)
    localUrl = null
  }

  async function handle(file: File | undefined) {
    if (!file || uploader.uploading) return
    revoke()
    localUrl = URL.createObjectURL(file)
    try {
      const asset = await uploader.upload(file)
      uploadedUrl = asset.url
      removed = false
      value = asset.id
      onuploaded?.(asset)
    } catch {
      // message is in `error`
    } finally {
      revoke()
    }
  }

  function onChange(event: Event) {
    const el = event.target as HTMLInputElement
    void handle(el.files?.[0])
    el.value = '' // allow re-selecting the same file
  }

  function onDrop(event: DragEvent) {
    event.preventDefault()
    dragging = false
    void handle(event.dataTransfer?.files?.[0])
  }

  function remove() {
    revoke()
    uploadedUrl = null
    removed = true
    uploader.clearError()
    value = null
  }

  $effect(() => () => revoke())
</script>

<div class="space-y-2">
  {#if label}<label for={inputId} class="text-sm leading-none font-medium">{label}</label>{/if}

  <!-- svelte-ignore a11y_no_static_element_interactions -->
  <div
    ondragover={(event: DragEvent) => { event.preventDefault(); dragging = true }}
    ondragleave={() => (dragging = false)}
    ondrop={onDrop}
  >
    {#if shown}
      <div class="relative">
        <img src={shown} alt="" class={cn('bg-muted mx-auto rounded-xl border transition-opacity', previewClass ?? 'max-h-48 object-contain', uploader.uploading && 'opacity-60')} />
        {#if uploader.uploading}
          <Loader2 class="absolute inset-0 m-auto size-6 animate-spin" aria-hidden="true" />
        {:else}
          <Button
            type="button" size="icon-sm" variant="secondary" class="absolute top-2 right-2 rounded-full"
            aria-label={`Remove ${label?.toLowerCase() ?? 'image'}`} onclick={remove}
          >
            <X />
          </Button>
        {/if}
      </div>
    {:else}
      <div
        class={cn(
          'text-muted-foreground flex min-h-36 flex-col items-center justify-center gap-1.5 rounded-xl border border-dashed p-4 text-center text-sm transition-colors',
          dragging ? 'border-brand bg-brand/5 text-foreground' : 'border-input bg-muted/30',
        )}
      >
        {#if uploader.uploading}<Loader2 class="size-5 animate-spin" aria-hidden="true" />{:else}<ImagePlus class="size-5" strokeWidth={1.75} aria-hidden="true" />{/if}
        <span>{uploader.uploading ? 'Uploading…' : dragging ? 'Drop to upload' : 'Drag an image here'}</span>
        <Button type="button" variant="outline" size="sm" class="mt-1 rounded-full" disabled={uploader.uploading} onclick={() => input?.click()}>Choose image</Button>
      </div>
    {/if}
  </div>

  <input
    id={inputId}
    bind:this={input}
    type="file"
    class="sr-only"
    aria-label={label ? undefined : 'Image'}
    accept="image/png,image/jpeg,image/webp,image/gif"
    disabled={uploader.uploading}
    onchange={onChange}
  />

  {#if shown && !uploader.uploading}
    <Button type="button" variant="outline" size="sm" class="rounded-full" onclick={() => input?.click()}>Replace image</Button>
  {/if}

  {#if uploader.uploading}
    <div class="bg-muted h-1.5 w-full overflow-hidden rounded-full" role="progressbar" aria-valuenow={uploader.progress} aria-valuemin="0" aria-valuemax="100">
      <div class="bg-primary h-full transition-all" style={`width:${uploader.progress}%`}></div>
    </div>
  {/if}
  {#if uploader.error}<p class="text-destructive text-sm" role="alert">{uploader.error}</p>{/if}
</div>
