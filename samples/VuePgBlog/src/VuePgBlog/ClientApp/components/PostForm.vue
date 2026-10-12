<script lang="ts">
export interface PostFormData {
  title: string
  body: string
  coverAssetId: string | null
  topics: string[]
}
</script>

<script setup lang="ts">
import { computed, ref } from 'vue'
import { Link, type InertiaPrecognitiveForm } from '@inertiajs/vue3'
import { Loader2 } from '@lucide/vue'
import ImageUpload from '@/components/ImageUpload.vue'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { Tabs, TabsContent, TabsList, TabsTrigger } from '@/components/ui/tabs'
import { Textarea } from '@/components/ui/textarea'
import { renderMarkdown } from '@/lib/markdown'

const props = defineProps<{
  form: InertiaPrecognitiveForm<PostFormData>
  submitLabel: string
  cancelHref: string
  coverPreviewUrl?: string | null
}>()
const emit = defineEmits<{ submit: [] }>()

const topics = ref(props.form.topics.join(', '))
const setTopics = (value: string | number) => {
  topics.value = String(value)
  props.form.topics = topics.value.split(',').map(x => x.trim()).filter(Boolean)
}
const uploading = ref(false)
const preview = computed(() => renderMarkdown(props.form.body))
const submit = () => {
  if (!props.form.processing && !uploading.value) emit('submit')
}
</script>

<template>
  <form class="flex flex-col gap-8" novalidate @submit.prevent="submit">
    <div class="flex flex-col gap-2">
      <ImageUpload v-model="form.coverAssetId" :preview-url="coverPreviewUrl" preview-class="aspect-[16/9] w-full object-cover" @uploading="uploading = $event" />
      <p v-if="form.errors.coverAssetId" class="text-destructive text-sm" role="alert">{{ form.errors.coverAssetId }}</p>
    </div>

    <div class="flex flex-col gap-1">
      <Label for="title" class="sr-only">Title</Label>
      <Input
        id="title" v-model="form.title" required maxlength="200" placeholder="Title" :aria-invalid="!!form.errors.title"
        aria-describedby="title-error" class="placeholder:text-muted-foreground/50 h-auto rounded-none border-0 bg-transparent px-0 py-1 font-serif text-3xl font-semibold tracking-tight shadow-none focus-visible:ring-0 md:text-3xl dark:bg-transparent"
        @change="form.validate('title')"
      />
      <p v-if="form.errors.title" id="title-error" class="text-destructive text-sm" role="alert">{{ form.errors.title }}</p>
    </div>

    <div class="flex flex-col gap-2">
      <Label for="topics">Topics</Label>
      <Input
        id="topics" :model-value="topics" placeholder="technology, books, travel" :aria-invalid="!!form.errors.topics" @update:model-value="setTopics"
        aria-describedby="topics-hint topics-error"
      />
      <p id="topics-hint" class="text-muted-foreground text-sm">Separate topics with commas, up to five.</p>
      <p v-if="form.errors.topics" id="topics-error" role="alert" class="text-destructive text-sm">{{ form.errors.topics }}</p>
    </div>

    <div class="flex flex-col gap-2">
      <Tabs default-value="write">
        <TabsList class="h-auto w-full justify-start gap-6 rounded-none border-b bg-transparent p-0">
          <TabsTrigger
            v-for="tab in ['write', 'preview']" :key="tab" :value="tab"
            class="text-muted-foreground hover:text-foreground data-[state=active]:text-foreground after:bg-foreground relative -mb-px h-auto flex-none rounded-none border-0 bg-transparent px-0 py-3 text-[15px] capitalize shadow-none after:absolute after:inset-x-0 after:bottom-0 after:h-0.5 after:origin-center after:scale-x-0 after:rounded-full after:transition-transform after:duration-300 data-[state=active]:bg-transparent data-[state=active]:shadow-none data-[state=active]:after:scale-x-100 dark:data-[state=active]:border-transparent dark:data-[state=active]:bg-transparent"
          >
            {{ tab }}
          </TabsTrigger>
        </TabsList>
        <TabsContent value="write" class="pt-2">
          <Label for="body" class="sr-only">Body (Markdown supported)</Label>
          <Textarea
            id="body" v-model="form.body" required placeholder="Write in Markdown" class="min-h-[26rem] resize-y rounded-xl font-mono text-sm leading-6"
            :aria-invalid="!!form.errors.body" aria-describedby="body-error" @change="form.validate('body')"
          />
        </TabsContent>
        <TabsContent value="preview" class="min-h-[26rem] pt-4">
          <div v-if="form.body.trim()" class="prose-post break-words" v-html="preview" />
          <p v-else class="text-muted-foreground text-sm">Nothing to preview yet.</p>
        </TabsContent>
      </Tabs>
      <p v-if="form.errors.body" id="body-error" class="text-destructive text-sm" role="alert">{{ form.errors.body }}</p>
    </div>

    <div class="bg-background/90 sticky bottom-[calc(4rem+env(safe-area-inset-bottom))] z-10 -mt-2 flex justify-end gap-2 border-t py-3 backdrop-blur lg:bottom-0">
      <Button variant="ghost" as-child class="rounded-lg"><Link :href="cancelHref">Cancel</Link></Button>
      <Button type="submit" class="rounded-lg active:scale-[0.98]" :disabled="form.processing || uploading">
        <Loader2 v-if="form.processing" class="animate-spin" aria-hidden="true" />
        {{ submitLabel }}
      </Button>
    </div>
  </form>
</template>
