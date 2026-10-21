import PostForm from '@/components/posts/PostForm'
import AppLayout from '@/layouts/AppLayout'

export default function Create() {
  return (
    <AppLayout title="New post">
      <div>
        <PostForm initial={{ title: '', body: '', coverUrl: null }} method="post" action="/posts" submitLabel="Publish" cancelHref="/posts" />
      </div>
    </AppLayout>
  )
}
