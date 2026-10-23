import { Link } from '@inertiajs/react'
import { CalendarDays, FileText, PenLine } from 'lucide-react'
import { EmptyState } from '@/components/EmptyState'
import FollowButton from '@/components/posts/FollowButton'
import VirtualList from '@/components/posts/VirtualList'
import PostCardItem from '@/components/posts/PostCardItem'
import { parseUtc } from '@/components/posts/time'
import { Avatar, AvatarFallback, AvatarImage } from '@/components/ui/avatar'
import { Button } from '@/components/ui/button'
import AppLayout, { initials } from '@/layouts/AppLayout'
import type { PostCard, UserSummary } from '@/types/posts'

const joined = (iso: string) => parseUtc(iso).toLocaleDateString('en', { month: 'long', year: 'numeric' })

export default function Profile({ user, posts }: { user: UserSummary; posts: { data: PostCard[] } }) {
  return (
    <AppLayout title={user.name}>
      <section className="flex flex-col gap-10">
        <header className="paper relative overflow-hidden rounded-3xl border px-6 pt-8 pb-6 sm:px-8">
          <div className="flex flex-wrap items-start justify-between gap-4">
            <Avatar className="size-20 ring-4 ring-background sm:size-24">
              {user.avatarUrl && <AvatarImage src={user.avatarUrl} alt="" />}
              <AvatarFallback className="text-xl">{initials(user.name)}</AvatarFallback>
            </Avatar>
            {user.isSelf ? (
              <div className="flex gap-2">
                <Button asChild variant="outline" className="h-10 rounded-full bg-background/70 px-5"><Link href="/settings">Edit profile</Link></Button>
                <Button asChild className="h-10 rounded-full px-5"><Link href="/posts/create"><PenLine aria-hidden /> Write</Link></Button>
              </div>
            ) : <FollowButton user={user} className="h-10 px-5" />}
          </div>
          <h1 className="mt-5 font-serif text-4xl leading-tight font-semibold tracking-[-0.02em] text-balance">{user.name}</h1>
          {user.bio ? (
            <p className="mt-2 max-w-xl text-[16px] leading-relaxed text-muted-foreground">{user.bio}</p>
          ) : user.isSelf ? (
            <p className="mt-2 text-sm text-muted-foreground">
              Add a short bio so readers know what you write about. <Link href="/settings" className="text-brand underline underline-offset-4">Add bio</Link>
            </p>
          ) : null}
          <div className="mt-5 flex flex-wrap items-center gap-x-5 gap-y-2 text-[15px] text-muted-foreground tabular-nums">
            <span><span className="font-semibold text-foreground">{user.followerCount.toLocaleString()}</span> {user.followerCount === 1 ? 'follower' : 'followers'}</span>
            <span><span className="font-semibold text-foreground">{user.followingCount.toLocaleString()}</span> following</span>
            <span className="inline-flex items-center gap-1.5 text-sm"><CalendarDays className="size-4" aria-hidden /> Joined {joined(user.joinedAt)}</span>
          </div>
        </header>

        <div>
          <h2 className="border-b pb-3 text-sm font-medium">Essays and reposts</h2>
          {posts.data.length ? (
            <VirtualList items={posts.data} data="posts" label={`${user.name}'s essays`} renderItem={p => <PostCardItem post={p} />} />
          ) : (
            <EmptyState
              icon={FileText} title="No essays yet"
              description={user.isSelf ? 'Your published essays will be listed here.' : `${user.name} has not published anything.`}
              action={user.isSelf ? <Button asChild size="sm" className="rounded-full"><Link href="/posts/create"><PenLine /> Write your first essay</Link></Button> : undefined}
            />
          )}
        </div>
      </section>
    </AppLayout>
  )
}
