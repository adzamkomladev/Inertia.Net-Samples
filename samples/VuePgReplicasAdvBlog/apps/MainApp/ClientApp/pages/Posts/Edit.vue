<script setup lang="ts">
import { Head, useForm } from '@inertiajs/vue3'
import PageHeader from '@/components/PageHeader.vue'
import PostForm, { type PostFormData } from '@/components/PostForm.vue'
import type { PostDetail } from '@/types'

const props = defineProps<{ post: PostDetail }>()

const form = useForm<PostFormData>('put', `/posts/${props.post.id}`, {
  topics: props.post.topics ?? [],
  title: props.post.title,
  body: props.post.body,
  coverAssetId: props.post.coverAssetId,
})
const submit = () => form.submit()
</script>

<template>
  <Head :title="`Edit: ${post.title}`" />
  <div class="animate-rise mx-auto flex max-w-2xl flex-col gap-8">
    <PageHeader title="Edit post" />
    <PostForm
      :form="form" submit-label="Save changes" :cancel-href="`/posts/${post.slug}`"
      :cover-preview-url="post.coverUrl" @submit="submit"
    />
  </div>
</template>
