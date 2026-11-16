import PostForm from '@/components/posts/PostForm'

export default function Create() {
  return (
    <>
      <div>
        <PostForm initial={{ title: '', body: '', coverUrl: null }} method="post" action="/posts" submitLabel="Publish" cancelHref="/posts" />
      </div>
    </>
  )
}

Create.layout = { title: 'New post', compose: true }
