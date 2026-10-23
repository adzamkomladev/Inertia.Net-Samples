import { Link, usePage } from '@inertiajs/react'
import { ArrowRight, Hash, Newspaper, PenLine, SearchX, Sparkles, X } from 'lucide-react'
import { cn } from 'cn'
import { EmptyState } from '@/components/EmptyState'
import { PageHeader } from '@/components/PageHeader'
import VirtualList from '@/components/posts/VirtualList'
import PostCardItem from '@/components/posts/PostCardItem'
import { Button } from '@/components/ui/button'
import AppLayout from '@/layouts/AppLayout'
import type { PostCard, TopicCount } from '@/types/posts'
import type { SharedProps } from '@/types/shared'

interface Props {
  posts: { data: PostCard[] }
  q: string
  topic: string | null
  topics?: TopicCount[]
}

const topicHref = (topic: string) => `/posts?topic=${encodeURIComponent(topic)}`

/** Landing block for visitors who are not signed in. */
function Hero() {
  return (
    <section className="paper relative mb-10 overflow-hidden rounded-3xl border px-6 py-10 sm:px-10 sm:py-14">
      <p className="flex items-center gap-2 text-sm font-medium text-brand"><Sparkles className="size-4" aria-hidden /> Essays worth your evening</p>
      <h1 className="mt-4 max-w-2xl font-serif text-[2.6rem] leading-[1.05] font-semibold tracking-[-0.03em] text-balance sm:text-6xl">
        Ideas, slowly told.
      </h1>
      <p className="mt-5 max-w-xl text-[17px] leading-relaxed text-muted-foreground">
        Inkwell is a home for long-form writing. Follow the writers you love, get a feed shaped by what you read,
        and publish your own essays in Markdown.
      </p>
      <div className="mt-8 flex flex-wrap gap-3">
        <Button asChild size="lg" className="group/cta h-11 rounded-full px-6 active:scale-[0.98]">
          <Link href="/register">Start writing <ArrowRight className="transition-transform group-hover/cta:translate-x-0.5" aria-hidden /></Link>
        </Button>
        <Button asChild size="lg" variant="outline" className="h-11 rounded-full bg-background/60 px-6">
          <Link href="/users">Meet the writers</Link>
        </Button>
      </div>
    </section>
  )
}

function TopicChip({ topic, count, active }: { topic: string; count?: number; active: boolean }) {
  return (
    <Link
      href={active ? '/posts' : topicHref(topic)} preserveScroll aria-current={active ? 'page' : undefined}
      className={cn(
        'inline-flex h-9 shrink-0 items-center gap-1.5 rounded-full border px-3.5 text-sm transition-[background-color,color,border-color,transform] active:scale-95',
        active ? 'border-foreground bg-foreground text-background' : 'bg-background text-muted-foreground hover:border-foreground/30 hover:text-foreground',
      )}
    >
      {topic}
      {count !== undefined && <span className={cn('text-xs tabular-nums', active ? 'text-background/70' : 'text-muted-foreground/70')}>{count.toLocaleString()}</span>}
      {active && <X className="size-3.5" aria-label="Clear topic" />}
    </Link>
  )
}

function Sidebar({ topics, topic }: { topics: TopicCount[]; topic: string | null }) {
  const user = usePage<SharedProps>().props.auth.user
  return (
    <aside className="hidden lg:block">
      <div className="sticky top-24 space-y-8">
        {topics.length > 0 && (
          <section aria-labelledby="topics-heading">
            <h2 id="topics-heading" className="mb-3 text-xs font-medium tracking-wide text-muted-foreground uppercase">Popular topics</h2>
            <div className="flex flex-wrap gap-2">
              {topics.map(t => <TopicChip key={t.topic} topic={t.topic} count={t.count} active={t.topic === topic} />)}
            </div>
          </section>
        )}
        <section className="rounded-2xl border bg-card p-5">
          <p className="font-serif text-xl leading-snug font-semibold">{user?.verified ? 'Have something to say?' : 'Write with us'}</p>
          <p className="mt-1.5 text-sm leading-6 text-muted-foreground">
            {user?.verified ? 'Drafts stay private until you publish. Markdown and images welcome.' : 'Create a free account to publish, follow writers and save posts for later.'}
          </p>
          <Button asChild size="sm" className="mt-4 rounded-full">
            <Link href={user?.verified ? '/posts/create' : user ? '/email/verify' : '/register'}><PenLine aria-hidden /> {user?.verified ? 'New post' : user ? 'Verify email' : 'Sign up'}</Link>
          </Button>
        </section>
      </div>
    </aside>
  )
}

export default function Index({ posts, q, topic, topics = [] }: Props) {
  const user = usePage<SharedProps>().props.auth.user
  const browsing = !q && !topic
  return (
    <AppLayout title={q ? `Results for “${q}”` : topic ? `#${topic}` : 'Posts'} wide>
      {!user && browsing && <Hero />}
      <div className="grid gap-12 lg:grid-cols-[minmax(0,1fr)_17rem]">
        <div className="min-w-0">
          <PageHeader
            title={q ? <>Results for <span className="font-serif italic">“{q}”</span></>
              : topic ? <span className="inline-flex items-center gap-1"><Hash className="size-7 text-brand sm:size-8" aria-hidden />{topic}</span>
                : user ? `Welcome back, ${user.name.split(' ')[0]}` : 'Latest essays'}
            description={q ? 'Ranked by relevance across the whole community.' : topic ? 'The newest essays on this topic.' : user ? 'The newest essays from across Inkwell.' : 'Newest first.'}
            actions={q || topic ? (
              <Button asChild variant="outline" size="sm" className="rounded-full"><Link href="/posts"><X /> Clear</Link></Button>
            ) : undefined}
          />

          {topics.length > 0 && !q && (
            <nav aria-label="Topics" className="-mx-4 mt-5 flex gap-2 overflow-x-auto px-4 pb-1 sm:-mx-6 sm:px-6 lg:hidden">
              {topics.map(t => <TopicChip key={t.topic} topic={t.topic} active={t.topic === topic} />)}
            </nav>
          )}

          <div className="mt-4 border-t">
            {posts.data.length === 0 ? (
              <EmptyState
                icon={q ? SearchX : Newspaper}
                title={q ? `No posts match “${q}”` : topic ? `Nothing on #${topic} yet` : 'No posts yet'}
                description={q ? 'Try a different keyword, or browse everything.' : topic ? 'Be the first to write about it.' : 'Be the first to publish something.'}
                action={q || topic ? (
                  <Button asChild variant="outline" size="sm" className="rounded-full"><Link href="/posts">Show all posts</Link></Button>
                ) : user?.verified ? (
                  <Button asChild size="sm" className="rounded-full"><Link href="/posts/create"><PenLine /> Write a post</Link></Button>
                ) : undefined}
              />
            ) : (
              <VirtualList key={`${q}|${topic}`} items={posts.data} data="posts" label="Posts" renderItem={p => <PostCardItem post={p} />} />
            )}
          </div>
        </div>
        <Sidebar topics={topics} topic={topic} />
      </div>
    </AppLayout>
  )
}
