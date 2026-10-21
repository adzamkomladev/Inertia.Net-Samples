import { Link, router, usePage } from '@inertiajs/react'
import { useState } from 'react'
import type { ReloadOptions } from '@inertiajs/core'
import { Newspaper, PenLine, SearchX, X } from 'lucide-react'
import { EmptyState } from '@/components/EmptyState'
import { PageHeader } from '@/components/PageHeader'
import VirtualList from '@/components/posts/VirtualList'
import PostCardItem from '@/components/posts/PostCardItem'
import { Button } from '@/components/ui/button'
import { Skeleton } from '@/components/ui/skeleton'
import AppLayout from '@/layouts/AppLayout'
import type { PostCard } from '@/types/posts'
import type { SharedProps } from '@/types/shared'

interface Props {
  posts: { data: PostCard[] }
  q: string
  searchEngine?: 'native' | 'meilisearch'
}

export default function Index({ posts, q, searchEngine }: Props) {
  const [resetting, setResetting] = useState(false)
  const pagination: ReloadOptions = {
    only: ['searchEngine'],
    data: q && searchEngine ? { engine: searchEngine } : {},
    onBeforeUpdate: page => {
      const actual = page.props.searchEngine
      if (q && searchEngine && actual && actual !== searchEngine) {
        setResetting(true)
        // Discard merged rankings before React renders them.
        page.props.posts = { ...(page.props.posts as { data: PostCard[] }), data: [] }
      }
    },
    onSuccess: page => {
      const actual = page.props.searchEngine
      if (q && searchEngine && actual && actual !== searchEngine) {
        router.get('/posts', { q, engine: String(actual) }, { reset: ['posts'], replace: true, preserveState: false, preserveScroll: false, onFinish: () => setResetting(false) })
      }
    },
  }
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
          {resetting ? (
            <div className="flex flex-col gap-6 py-5" aria-busy="true">
              {[0, 1, 2].map(i => (
                <div key={i} className="flex gap-4">
                  <Skeleton className="size-10 rounded-full" />
                  <div className="flex-1 space-y-2.5"><Skeleton className="h-3.5 w-1/3" /><Skeleton className="h-5 w-4/5" /><Skeleton className="h-3.5 w-full" /></div>
                </div>
              ))}
              <p role="status" className="sr-only">Refreshing search results…</p>
            </div>
          ) : posts.data.length === 0 ? (
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
            <VirtualList key={`${q}:${searchEngine ?? 'native'}`} params={pagination} items={posts.data} data="posts" label="Posts" renderItem={p => <PostCardItem post={p} />} />
          )}
        </div>
      </div>
    </AppLayout>
  )
}
