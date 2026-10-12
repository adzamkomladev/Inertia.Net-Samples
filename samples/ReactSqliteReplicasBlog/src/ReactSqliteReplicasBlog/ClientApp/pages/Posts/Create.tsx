import { PageHeader } from '@/components/PageHeader'
import PostForm from '@/components/posts/PostForm'
import AppLayout from '@/layouts/AppLayout'

export default function Create() {
  return (
    <AppLayout title="New post">
      <div className="mx-auto flex max-w-2xl flex-col gap-8">
        <PageHeader title="New post" />
        <PostForm initial={{ title: '', body: '', coverUrl: null }} method="post" action="/posts" submitLabel="Publish" cancelHref="/posts" />
      </div>
    </AppLayout>
  )
}
