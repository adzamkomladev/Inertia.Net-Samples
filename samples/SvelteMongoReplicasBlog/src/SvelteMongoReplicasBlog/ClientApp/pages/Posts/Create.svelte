<script lang="ts">
  import { useForm } from '@inertiajs/svelte'
  import PageHeader from '@/components/PageHeader.svelte'
  import PageTitle from '@/components/PageTitle.svelte'
  import PostForm, { type PostFormData } from '@/components/PostForm.svelte'

  const form = useForm<PostFormData>('post', '/posts', { title: '', body: '', coverAssetId: null, topics: [] })
  const submit = () => form.submit()
</script>

<PageTitle title="Write a post" />
<div class="animate-rise mx-auto flex max-w-2xl flex-col gap-8">
  <PageHeader title="Write a post" />
  <PostForm {form} submitLabel="Publish" cancelHref="/posts" onsubmit={submit} />
</div>
