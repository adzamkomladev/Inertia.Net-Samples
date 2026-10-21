import { Link, usePage } from '@inertiajs/react'
import { Newspaper, PenLine, SearchX, X } from 'lucide-react'
import { EmptyState } from '@/components/EmptyState'
import { PageHeader } from '@/components/PageHeader'
import VirtualList from '@/components/posts/VirtualList'
import PostCardItem from '@/components/posts/PostCardItem'
import { Button } from '@/components/ui/button'
import AppLayout from '@/layouts/AppLayout'
import type { PostCard } from '@/types/posts'
import type { SharedProps } from '@/types/shared'

interface Props {
  posts: { data: PostCard[] }
  q: string
}

export default function Index({ posts, q }: Props) {
  const user = usePage<SharedProps>().props.auth.user
  return (
    <AppLayout title={q ? `Results for “${q}”` : 'Posts'}>
      <div className="mx-auto max-w-2xl">
        <PageHeader
          title={q ? <>Results for <span className="font-serif italic">“{q}”</span></> : 'Latest'}
          description={q ? 'Ranked by relevance across the whole community.' : 'Fresh writing from the community, newest first.'}
          actions={q ? (
            <Button asChild variant="outline" size="sm" className="rounded-full"><Link href="/posts"><X /> Clear</Link></Button>
          ) : user?.verified ? (
            <Button asChild size="sm" className="rounded-full lg:hidden"><Link href="/posts/create"><PenLine /> Write</Link></Button>
          ) : undefined}
        />

        <div className="mt-4 border-t">
          {posts.data.length === 0 ? (
            <EmptyState
              icon={q ? SearchX : Newspaper}
              title={q ? `No posts match “${q}”` : 'No posts yet'}
              description={q ? 'Try a different keyword, or browse everything.' : 'Be the first to publish something.'}
              action={q ? (
                <Button asChild variant="outline" size="sm" className="rounded-full"><Link href="/posts">Show all posts</Link></Button>
              ) : user?.verified ? (
                <Button asChild size="sm" className="rounded-full"><Link href="/posts/create"><PenLine /> Write a post</Link></Button>
              ) : undefined}
            />
          ) : (
            <VirtualList key={q} items={posts.data} data="posts" label="Posts" renderItem={p => <PostCardItem post={p} />} />
          )}
        </div>
      </div>
    </AppLayout>
  )
}
