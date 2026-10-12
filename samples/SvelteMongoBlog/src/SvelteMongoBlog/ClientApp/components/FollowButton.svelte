<script lang="ts">
  import { usePage } from '@inertiajs/svelte'
  import Button from '@/components/ui/button/Button.svelte'
  import { useSocialToggle } from '@/lib/social-toggle.svelte'
  import { cn } from '@/lib/utils'
  import type { AuthUser, UserSummary } from '@/types'

  let { user }: { user: UserSummary } = $props()

  const page = usePage()
  const viewer = $derived((page.props.auth as { user: AuthUser | null } | undefined)?.user ?? null)
  const follow = useSocialToggle({
    url: () => `/users/${user.id}/follow`,
    kind: 'user',
    id: () => user.id,
    field: 'following',
    active: () => user.following,
    count: () => user.followerCount,
    countField: 'followerCount',
  })
</script>

{#if viewer && viewer.id !== user.id}
  <Button
    type="button"
    size="sm"
    variant={follow.active ? 'outline' : 'default'}
    disabled={follow.pending}
    aria-pressed={follow.active}
    class={cn('group/follow h-8 min-w-24 rounded-full px-4', follow.active && 'hover:border-destructive/40 hover:bg-destructive/10 hover:text-destructive')}
    onclick={follow.toggle}
  >
    {#if follow.active}
      <span class="group-hover/follow:hidden">Following</span>
      <span class="hidden group-hover/follow:inline">Unfollow</span>
    {:else}
      Follow
    {/if}
  </Button>
{/if}
