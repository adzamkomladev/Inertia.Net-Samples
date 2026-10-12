<script lang="ts">
  import { FileText } from '@lucide/svelte'
  import EmptyState from '@/components/EmptyState.svelte'
  import FollowButton from '@/components/FollowButton.svelte'
  import PageTitle from '@/components/PageTitle.svelte'
  import VirtualPostList from '@/components/VirtualPostList.svelte'
  import Avatar from '@/components/ui/avatar/Avatar.svelte'
  import Button from '@/components/ui/button/Button.svelte'
  import type { PostSummary, ScrollPage, UserSummary } from '@/types'

  let { user, posts }: { user: UserSummary; posts: ScrollPage<PostSummary> } = $props()

  const initials = $derived(user.name.split(/\s+/).map(p => p[0]).slice(0, 2).join('').toUpperCase())
</script>

<PageTitle title={user.name} />
<section class="mx-auto flex max-w-2xl flex-col gap-6">
  <header class="flex flex-wrap items-center gap-x-5 gap-y-4">
    <Avatar src={user.avatarUrl} fallback={initials} class="size-20" fallbackClass="text-xl" />
    <div class="min-w-0 flex-1 space-y-1">
      <h1 class="truncate text-2xl font-semibold tracking-tight">{user.name}</h1>
      <p class="text-muted-foreground flex gap-4 text-sm tabular-nums">
        <span><span class="text-foreground font-medium">{user.followerCount}</span> followers</span>
        <span><span class="text-foreground font-medium">{user.followingCount}</span> following</span>
      </p>
    </div>
    {#if user.isSelf}
      <Button href="/settings/preferences" variant="outline" size="sm" class="rounded-full">Edit profile</Button>
    {:else}
      <FollowButton {user} />
    {/if}
  </header>

  <div>
    <h2 class="border-b pb-3 text-base font-semibold">Posts</h2>
    {#if posts.data.length}
      <VirtualPostList {posts} />
    {:else}
      <EmptyState icon={FileText} title="No posts yet" description={`${user.name} has not published anything.`} />
    {/if}
  </div>
</section>
