<script lang="ts" module>
  export interface PostFormData {
    title: string
    body: string
    coverAssetId: string | null
    topics: string[]
  }
</script>

<script lang="ts">
  import type { InertiaPrecognitiveForm } from '@inertiajs/svelte'
  import { Loader2 } from '@lucide/svelte'
  import ImageUpload from '@/components/ImageUpload.svelte'
  import Button from '@/components/ui/button/Button.svelte'
  import Input from '@/components/ui/input/Input.svelte'
  import Label from '@/components/ui/label/Label.svelte'
  import Textarea from '@/components/ui/textarea/Textarea.svelte'
  import { renderMarkdown } from '@/lib/markdown'
  import { cn } from '@/lib/utils'

  let {
    form,
    submitLabel,
    cancelHref,
    coverPreviewUrl,
    onsubmit,
  }: {
    form: InertiaPrecognitiveForm<PostFormData>
    submitLabel: string
    cancelHref: string
    coverPreviewUrl?: string | null
    onsubmit: () => void
  } = $props()

  // svelte-ignore state_referenced_locally
  let topicsText = $state(form.topics.join(', '))
  const setTopics = (value: string) => {
    topicsText = value
    form.topics = value.split(',').map(x => x.trim()).filter(Boolean)
  }
  let uploading = $state(false)
  let tab = $state<'write' | 'preview'>('write')
  const preview = $derived(renderMarkdown(form.body))
  const submit = () => {
    if (!form.processing && !uploading) onsubmit()
  }

  const tabClass = (value: 'write' | 'preview') => cn(
    'text-muted-foreground hover:text-foreground relative -mb-px h-auto flex-none rounded-none border-0 bg-transparent px-0 py-3 text-[15px] capitalize shadow-none transition-colors',
    'after:bg-foreground after:absolute after:inset-x-0 after:bottom-0 after:h-0.5 after:origin-center after:scale-x-0 after:rounded-full after:transition-transform after:duration-300',
    tab === value && 'text-foreground after:scale-x-100',
  )
</script>

<form class="flex flex-col gap-8" novalidate onsubmit={(event: SubmitEvent) => { event.preventDefault(); submit() }}>
  <div class="flex flex-col gap-2">
    <ImageUpload bind:value={form.coverAssetId} previewUrl={coverPreviewUrl} previewClass="aspect-[16/9] w-full object-cover" onuploadingchange={(value) => (uploading = value)} />
    {#if form.errors.coverAssetId}<p class="text-destructive text-sm" role="alert">{form.errors.coverAssetId}</p>{/if}
  </div>

  <div class="flex flex-col gap-1">
    <Label for="title" class="sr-only">Title</Label>
    <Input
      id="title" bind:value={form.title} required maxlength="200" placeholder="Title" aria-invalid={!!form.errors.title}
      aria-describedby="title-error" class="placeholder:text-muted-foreground/50 h-auto rounded-none border-0 bg-transparent px-0 py-1 font-serif text-3xl font-semibold tracking-tight shadow-none focus-visible:ring-0 md:text-3xl dark:bg-transparent"
      onchange={() => form.validate('title')}
    />
    {#if form.errors.title}<p id="title-error" class="text-destructive text-sm" role="alert">{form.errors.title}</p>{/if}
  </div>

  <div class="flex flex-col gap-2">
    <Label for="topics">Topics</Label>
    <Input
      id="topics" value={topicsText} placeholder="technology, books, travel" aria-invalid={!!form.errors.topics}
      oninput={(event: Event) => setTopics((event.currentTarget as HTMLInputElement).value)}
      aria-describedby="topics-hint topics-error"
    />
    <p id="topics-hint" class="text-muted-foreground text-sm">Separate topics with commas, up to five.</p>
    {#if form.errors.topics}<p id="topics-error" role="alert" class="text-destructive text-sm">{form.errors.topics}</p>{/if}
  </div>

  <div class="flex flex-col gap-2">
    <div role="tablist" aria-label="Editor mode" class="flex w-full justify-start gap-6 border-b">
      {#each ['write', 'preview'] as value (value)}
        <button type="button" role="tab" aria-selected={tab === value} class={tabClass(value as 'write' | 'preview')} onclick={() => (tab = value as 'write' | 'preview')}>
          {value}
        </button>
      {/each}
    </div>
    {#if tab === 'write'}
      <div class="pt-2">
        <Label for="body" class="sr-only">Body (Markdown supported)</Label>
        <Textarea
          id="body" bind:value={form.body} required placeholder="Write in Markdown" class="min-h-[26rem] resize-y rounded-xl font-mono text-sm leading-6"
          aria-invalid={!!form.errors.body} aria-describedby="body-error" onchange={() => form.validate('body')}
        />
      </div>
    {:else}
      <div class="min-h-[26rem] pt-4">
        {#if form.body.trim()}
          <div class="prose-post break-words">{@html preview}</div>
        {:else}
          <p class="text-muted-foreground text-sm">Nothing to preview yet.</p>
        {/if}
      </div>
    {/if}
    {#if form.errors.body}<p id="body-error" class="text-destructive text-sm" role="alert">{form.errors.body}</p>{/if}
  </div>

  <div class="bg-background/90 sticky bottom-[calc(4rem+env(safe-area-inset-bottom))] z-10 -mt-2 flex justify-end gap-2 border-t py-3 backdrop-blur lg:bottom-0">
    <Button variant="ghost" href={cancelHref} class="rounded-lg">Cancel</Button>
    <Button type="submit" class="rounded-lg active:scale-[0.98]" disabled={form.processing || uploading}>
      {#if form.processing}<Loader2 class="animate-spin" aria-hidden="true" />{/if}
      {submitLabel}
    </Button>
  </div>
</form>
