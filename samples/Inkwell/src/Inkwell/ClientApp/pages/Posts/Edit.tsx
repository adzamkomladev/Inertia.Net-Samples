import PostForm from '@/components/posts/PostForm'
import type { PostEdit } from '@/types/posts'

export default function Edit({ post }: { post: PostEdit }) {
  return (
    <>
      <div>
        <h1 className="sr-only">Editing {post.title}</h1>
        <PostForm
          initial={{ title: post.title, body: post.body, coverUrl: post.coverUrl, topics: post.topics ?? [], isPublished: post.isPublished ?? true }}
          method="put"
          action={`/posts/${post.id}`}
          submitLabel="Save changes"
          cancelHref={`/posts/${post.id}`}
        />
      </div>
    </>
  )
}

Edit.layout = { title: 'Edit post', compose: true }
