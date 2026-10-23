import { Link, router } from '@inertiajs/react'
import { Search, UserRoundSearch, X } from 'lucide-react'
import { useState, type FormEvent } from 'react'
import { EmptyState } from '@/components/EmptyState'
import { PageHeader } from '@/components/PageHeader'
import FollowButton from '@/components/posts/FollowButton'
import VirtualList from '@/components/posts/VirtualList'
import { Avatar, AvatarFallback, AvatarImage } from '@/components/ui/avatar'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import AppLayout, { initials } from '@/layouts/AppLayout'
import { useDebouncedSearch } from '@/hooks/use-debounced-search'
import type { UserSummary } from '@/types/posts'

function PersonRow({ person }: { person: UserSummary }) {
  return (
    <article className="group/person relative -mx-3 flex items-center gap-4 rounded-2xl px-3 py-4 transition-colors after:pointer-events-none after:absolute after:inset-x-3 after:bottom-0 after:h-px after:bg-border hover:bg-accent/45 hover:after:opacity-0">
      <Link href={`/users/${person.id}`} aria-label={`View ${person.name}'s profile`} className="shrink-0 rounded-full outline-none focus-visible:ring-2 focus-visible:ring-ring">
        <Avatar className="size-12 ring-2 ring-background transition-transform duration-300 group-hover/person:scale-105">
          {person.avatarUrl && <AvatarImage src={person.avatarUrl} alt="" />}
          <AvatarFallback>{initials(person.name)}</AvatarFallback>
        </Avatar>
      </Link>
      <div className="min-w-0 flex-1">
        <Link href={`/users/${person.id}`} className="block truncate font-medium hover:underline">
          {person.name}{person.isSelf && <span className="ml-2 rounded-full bg-muted px-2 py-0.5 text-xs font-normal text-muted-foreground">You</span>}
        </Link>
        {person.bio && <p className="mt-0.5 line-clamp-1 text-sm text-muted-foreground">{person.bio}</p>}
        <p className="mt-0.5 truncate text-xs text-muted-foreground tabular-nums">{person.followerCount.toLocaleString()} followers · {person.followingCount.toLocaleString()} following</p>
      </div>
      <FollowButton user={person} />
    </article>
  )
}

export default function People({ users, filters }: { users: { data: UserSummary[] }; filters: { q: string } }) {
  const [q, setQ] = useState(filters.q)
  useDebouncedSearch(q, '/users')

  const search = (event: FormEvent) => {
    event.preventDefault()
    router.get('/users', q.trim() ? { q: q.trim() } : {}, { replace: true, preserveState: true, preserveScroll: true })
  }

  return (
    <AppLayout title="People">
      <section className="flex w-full flex-col gap-6">
        <PageHeader title="People" description="Find writers to follow. Their essays shape your feed." />

        <form className="relative" onSubmit={search} role="search">
          <Search className="pointer-events-none absolute top-1/2 left-3 size-4 -translate-y-1/2 text-muted-foreground" aria-hidden />
          <Input
            value={q} onChange={e => setQ(e.target.value)} placeholder="Search people by name…"
            aria-label="Search people" autoComplete="off" className="h-11 rounded-full bg-muted/50 pr-10 pl-10 transition-colors focus-visible:bg-background"
          />
          {q && <button type="button" aria-label="Clear search" className="absolute top-1/2 right-2 -translate-y-1/2 rounded-md p-1 text-muted-foreground hover:text-foreground focus-visible:ring-2 focus-visible:ring-ring" onClick={() => setQ('')}><X className="size-4" aria-hidden /></button>}
        </form>

        {users.data.length ? (
          <VirtualList items={users.data} data="users" label="People" renderItem={person => <PersonRow person={person} />} />
        ) : (
          <EmptyState
            icon={UserRoundSearch}
            title={filters.q ? `No people match “${filters.q}”` : 'No people yet'}
            description={filters.q ? 'Try a different name.' : 'People appear here once they join.'}
            action={filters.q ? <Button variant="link" onClick={() => { setQ(''); router.get('/users', {}, { replace: true }) }}>Clear search</Button> : undefined}
          />
        )}
      </section>
    </AppLayout>
  )
}
