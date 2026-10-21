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
      <section className="flex flex-col gap-10">
        <header className="flex flex-col items-start gap-5">
          <Avatar className="size-20">
            {user.avatarUrl && <AvatarImage src={user.avatarUrl} alt="" />}
            <AvatarFallback className="text-xl">{initials(user.name)}</AvatarFallback>
          </Avatar>
          <div className="space-y-2">
            <h1 className="font-serif text-4xl leading-tight font-semibold tracking-[-0.02em] text-balance">{user.name}</h1>
            <p className="flex gap-4 text-[15px] text-muted-foreground tabular-nums">
              <span><span className="font-medium text-foreground">{user.followerCount.toLocaleString()}</span> {user.followerCount === 1 ? 'follower' : 'followers'}</span>
              <span><span className="font-medium text-foreground">{user.followingCount.toLocaleString()}</span> following</span>
            </p>
          </div>
          {user.isSelf ? (
            <Button asChild variant="outline" className="h-10 rounded-full px-5"><Link href="/settings">Edit profile</Link></Button>
          ) : <FollowButton user={user} className="h-10 px-5" />}
        </header>

        <div>
          <h2 className="border-b pb-3 text-sm font-medium">Essays</h2>
          {posts.data.length ? (
            <VirtualList items={posts.data} data="posts" label={`${user.name}'s essays`} renderItem={p => <PostCardItem post={p} />} />
          ) : (
            <EmptyState icon={FileText} title="No essays yet" description={`${user.name} has not published anything.`} />
          )}
        </div>
      </section>
    </AppLayout>
  )
}
