import { Link } from '@inertiajs/react'
import { Bookmark, Compass, Newspaper } from 'lucide-react'
import { EmptyState } from '@/components/EmptyState'
import { LinkTabs, PageHeader } from '@/components/PageHeader'
import VirtualList from '@/components/posts/VirtualList'
import PostCardItem from '@/components/posts/PostCardItem'
import { Button } from '@/components/ui/button'
import AppLayout from '@/layouts/AppLayout'
import type { PostCard } from '@/types/posts'

const descriptions: Record<string, string> = {
  'for-you': 'Picked from the people and topics you engage with.',
  following: 'The latest from people you follow.',
  bookmarks: 'Posts you saved for later.',
  mine: 'Everything you have published.',
  archives: 'Hidden from feeds. Restore a post any time from its menu.',
}

export default function Feed({ posts, title, mode }: { posts: { data: PostCard[] }; title: string; mode: string }) {
  const ranked = mode === 'for-you' || mode === 'following'
  const saved = mode === 'bookmarks'
  return (
    <AppLayout title={title}>
      <section>
        <PageHeader title={ranked ? 'Your feed' : title} description={descriptions[mode]} />
        {ranked && (
          <LinkTabs
            label="Feed mode" current={mode} className="mt-5"
            tabs={[
              { href: '/feed?mode=for-you', label: 'For you', value: 'for-you' },
              { href: '/feed?mode=following', label: 'Following', value: 'following' },
            ]}
          />
        )}
        <div className={ranked ? '' : 'mt-4 border-t'}>
          {posts.data.length ? (
            <VirtualList items={posts.data} data="posts" label="Posts" renderItem={p => <PostCardItem post={p} />} />
          ) : (
            <EmptyState
              icon={saved ? Bookmark : ranked ? Compass : Newspaper}
              title={saved ? 'No bookmarks yet' : mode === 'mine' ? 'You have not published anything yet' : mode === 'archives' ? 'No archived posts' : mode === 'following' ? 'Your following feed is empty' : 'Nothing here yet'}
              description={saved ? 'Tap the bookmark on any post to keep it here.' : ranked ? 'Follow a few writers or like some posts and this feed will fill up.' : 'New posts will show up here as soon as they are published.'}
              action={<Button asChild variant="outline" size="sm" className="rounded-full"><Link href={saved ? '/posts' : '/users'}>{saved ? 'Browse posts' : 'Find people to follow'}</Link></Button>}
            />
          )}
        </div>
      </section>
    </AppLayout>
  )
}
