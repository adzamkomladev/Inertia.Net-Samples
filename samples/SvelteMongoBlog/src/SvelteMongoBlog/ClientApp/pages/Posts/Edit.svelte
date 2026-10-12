<script lang="ts">
  import { useForm } from '@inertiajs/svelte'
  import PageHeader from '@/components/PageHeader.svelte'
  import PageTitle from '@/components/PageTitle.svelte'
  import PostForm, { type PostFormData } from '@/components/PostForm.svelte'
  import type { PostDetail } from '@/types'

  let { post }: { post: PostDetail } = $props()

  // svelte-ignore state_referenced_locally
  const form = useForm<PostFormData>('put', `/posts/${post.id}`, {
    topics: post.topics ?? [],
    title: post.title,
    body: post.body,
    coverAssetId: post.coverAssetId,
  })
  const submit = () => form.submit()
</script>

<PageTitle title={`Edit: ${post.title}`} />
<div class="animate-rise mx-auto flex max-w-2xl flex-col gap-8">
  <PageHeader title="Edit post" />
  <PostForm {form} submitLabel="Save changes" cancelHref={`/posts/${post.slug}`} coverPreviewUrl={post.coverUrl} onsubmit={submit} />
</div>
