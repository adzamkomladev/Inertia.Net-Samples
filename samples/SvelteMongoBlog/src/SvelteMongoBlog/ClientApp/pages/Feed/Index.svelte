<script lang="ts">
  import { Bookmark, Compass, Newspaper } from '@lucide/svelte'
  import EmptyState from '@/components/EmptyState.svelte'
  import LinkTabs from '@/components/LinkTabs.svelte'
  import PageHeader from '@/components/PageHeader.svelte'
  import PageTitle from '@/components/PageTitle.svelte'
  import VirtualPostList from '@/components/VirtualPostList.svelte'
  import Button from '@/components/ui/button/Button.svelte'
  import type { PostSummary, ScrollPage } from '@/types'

  let { posts, title, mode }: { posts: ScrollPage<PostSummary>; title: string; mode: string; filters: { q: string } } = $props()

  const descriptions: Record<string, string> = {
    'for-you': 'Picked from the people and topics you engage with.',
    following: 'The latest from people you follow.',
    bookmarks: 'Posts you saved for later.',
    mine: 'Everything you have published.',
    archives: 'Hidden from feeds. Restore a post any time from its menu.',
  }
  const tabs = [
    { href: '/feed?mode=for-you', label: 'For you', value: 'for-you' },
    { href: '/feed?mode=following', label: 'Following', value: 'following' },
  ]
  const ranked = $derived(mode === 'for-you' || mode === 'following')
  const saved = $derived(mode === 'bookmarks')
  const emptyTitle = $derived(({
    bookmarks: 'No bookmarks yet',
    mine: 'You have not published anything yet',
    archives: 'No archived posts',
    following: 'Your following feed is empty',
  } as Record<string, string>)[mode] ?? 'Nothing here yet')
</script>

<PageTitle {title} />
<section class="mx-auto max-w-2xl">
  <PageHeader title={ranked ? 'Your feed' : title} description={descriptions[mode]} />
  {#if ranked}
    <div class="mt-5"><LinkTabs label="Feed mode" current={mode} {tabs} /></div>
  {/if}
  <div class={ranked ? '' : 'mt-4 border-t'}>
    {#if posts.data.length}
      <VirtualPostList {posts} />
    {:else}
      <EmptyState
        icon={saved ? Bookmark : ranked ? Compass : Newspaper} title={emptyTitle}
        description={saved ? 'Tap the bookmark on any post to keep it here.' : ranked ? 'Follow a few writers or like some posts and this feed will fill up.' : 'New posts will show up here as soon as they are published.'}
      >
        <Button href={saved ? '/posts' : '/users'} variant="outline" size="sm" class="rounded-full">{saved ? 'Browse posts' : 'Find people to follow'}</Button>
      </EmptyState>
    {/if}
  </div>
</section>
