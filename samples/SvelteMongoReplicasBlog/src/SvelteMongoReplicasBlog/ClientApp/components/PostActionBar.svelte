<script lang="ts">
  import { router, usePage } from '@inertiajs/svelte'
  import { Bookmark, Heart, MessageCircle, Repeat2 } from '@lucide/svelte'
  import SocialAction from '@/components/SocialAction.svelte'
  import { useSocialToggle } from '@/lib/social-toggle.svelte'
  import { cn } from '@/lib/utils'
  import type { AuthUser, PostSummary } from '@/types'

  let { post, showBookmark = true, class: className }: { post: PostSummary; showBookmark?: boolean; class?: string } = $props()

  const page = usePage()
  const user = $derived((page.props.auth as { user: AuthUser | null } | undefined)?.user ?? null)
  const verified = $derived(!!user?.verified)

  const like = useSocialToggle({ url: () => `/posts/${post.id}/like`, id: () => post.id, field: 'liked', active: () => !!post.liked, count: () => post.likeCount, countField: 'likeCount' })
  const repost = useSocialToggle({ url: () => `/posts/${post.id}/repost`, id: () => post.id, field: 'reposted', active: () => !!post.reposted, count: () => post.repostCount, countField: 'repostCount' })
  const bookmark = useSocialToggle({ url: () => `/posts/${post.id}/bookmark`, id: () => post.id, field: 'bookmarked', active: () => !!post.bookmarked })

  const requireVerified = (action: () => void) => (verified ? action() : router.visit('/email/verify'))
</script>

<div class={cn('flex items-center gap-5 sm:gap-8', className)}>
  <SocialAction label={post.commentCount ? `${post.commentCount} comments` : 'Comment'} count={post.commentCount} tone="comment" href={`/posts/${post.slug}#comments`}>
    <MessageCircle strokeWidth={1.75} />
  </SocialAction>

  {#if user}
    <SocialAction
      label={!verified ? 'Verify email to repost' : repost.active ? 'Undo repost' : 'Repost'} count={repost.count} active={repost.active}
      tone="repost" disabled={!!post.archivedAt || repost.pending} onclick={() => requireVerified(repost.toggle)}
    >
      <Repeat2 strokeWidth={1.75} />
    </SocialAction>
    <SocialAction
      label={!verified ? 'Verify email to like' : like.active ? 'Unlike' : 'Like'} count={like.count} active={like.active}
      tone="like" disabled={!!post.archivedAt || like.pending} onclick={() => requireVerified(like.toggle)}
    >
      <Heart strokeWidth={1.75} />
    </SocialAction>
    {#if showBookmark}
      <span class="ml-auto">
        <SocialAction
          label={bookmark.active ? 'Remove bookmark' : 'Bookmark'} active={bookmark.active}
          tone="bookmark" disabled={bookmark.pending} onclick={bookmark.toggle}
        >
          <Bookmark strokeWidth={1.75} />
        </SocialAction>
      </span>
    {/if}
  {:else}
    <SocialAction label="Log in to repost" count={post.repostCount} tone="repost" href="/login"><Repeat2 strokeWidth={1.75} /></SocialAction>
    <SocialAction label="Log in to like" count={post.likeCount} tone="like" href="/login"><Heart strokeWidth={1.75} /></SocialAction>
    {#if showBookmark}<span class="ml-auto"><SocialAction label="Log in to bookmark" tone="bookmark" href="/login"><Bookmark strokeWidth={1.75} /></SocialAction></span>{/if}
  {/if}
</div>
