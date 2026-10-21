import PostForm from '@/components/posts/PostForm'
import AppLayout from '@/layouts/AppLayout'
import type { PostEdit } from '@/types/posts'

export default function Edit({ post }: { post: PostEdit }) {
  return (
    <AppLayout title="Edit post">
      <div>
        <PostForm
          initial={{ title: post.title, body: post.body, coverUrl: post.coverUrl, topics: post.topics ?? [], isPublished: post.isPublished ?? true }}
          method="put"
          action={`/posts/${post.id}`}
          submitLabel="Save changes"
          cancelHref={`/posts/${post.id}`}
        />
      </div>
    </AppLayout>
  )
}
