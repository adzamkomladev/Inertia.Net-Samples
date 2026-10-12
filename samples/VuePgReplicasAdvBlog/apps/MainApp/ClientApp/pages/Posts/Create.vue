<script setup lang="ts">
import { Head, useForm } from '@inertiajs/vue3'
import PageHeader from '@/components/PageHeader.vue'
import PostForm, { type PostFormData } from '@/components/PostForm.vue'

const form = useForm<PostFormData>('post', '/posts', { title: '', body: '', coverAssetId: null, topics: [] })
const submit = () => form.submit()
</script>

<template>
  <Head title="Write a post" />
  <div class="animate-rise mx-auto flex max-w-2xl flex-col gap-8">
    <PageHeader title="Write a post" />
    <PostForm :form="form" submit-label="Publish" cancel-href="/posts" @submit="submit" />
  </div>
</template>
