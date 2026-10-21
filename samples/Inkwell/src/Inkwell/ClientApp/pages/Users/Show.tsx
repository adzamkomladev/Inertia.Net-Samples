import { Link } from '@inertiajs/react'
import { FileText } from 'lucide-react'
import { EmptyState } from '@/components/EmptyState'
import FollowButton from '@/components/posts/FollowButton'
import VirtualList from '@/components/posts/VirtualList'
import PostCardItem from '@/components/posts/PostCardItem'
import { Avatar, AvatarFallback, AvatarImage } from '@/components/ui/avatar'
import { Button } from '@/components/ui/button'
import AppLayout, { initials } from '@/layouts/AppLayout'
import type { PostCard, UserSummary } from '@/types/posts'

export default function Profile({ user, posts }: { user: UserSummary; posts: { data: PostCard[] } }) {
  return (
    <AppLayout title={user.name}>
      <section className="mx-auto flex max-w-2xl flex-col gap-6">
        <header className="flex flex-wrap items-center gap-x-5 gap-y-4">
          <Avatar className="size-20">
            {user.avatarUrl && <AvatarImage src={user.avatarUrl} alt="" />}
            <AvatarFallback className="text-xl">{initials(user.name)}</AvatarFallback>
          </Avatar>
          <div className="min-w-0 flex-1 space-y-1">
            <h1 className="truncate text-2xl font-semibold tracking-tight">{user.name}</h1>
            <p className="flex gap-4 text-sm text-muted-foreground tabular-nums">
              <span><span className="font-medium text-foreground">{user.followerCount}</span> followers</span>
              <span><span className="font-medium text-foreground">{user.followingCount}</span> following</span>
            </p>
          </div>
          {user.isSelf ? (
            <Button asChild variant="outline" size="sm" className="rounded-full">
              <Link href="/settings">Edit profile</Link>
            </Button>
          ) : <FollowButton user={user} />}
        </header>

        <div>
          <h2 className="border-b pb-3 text-base font-semibold">Posts</h2>
          {posts.data.length ? (
            <VirtualList items={posts.data} data="posts" label={`${user.name}'s posts`} renderItem={p => <PostCardItem post={p} />} />
          ) : (
            <EmptyState icon={FileText} title="No posts yet" description={`${user.name} has not published anything.`} />
          )}
        </div>
      </section>
    </AppLayout>
  )
}
