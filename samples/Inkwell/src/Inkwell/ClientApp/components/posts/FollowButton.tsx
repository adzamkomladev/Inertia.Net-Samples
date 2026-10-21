import { usePage } from '@inertiajs/react'
import { cn } from 'cn'
import { Button } from '@/components/ui/button'
import { useSocialToggle } from '@/hooks/use-social-mutation'
import type { UserSummary } from '@/types/posts'
import type { SharedProps } from '@/types/shared'

/** Follow pill that flips to a destructive "Unfollow" affordance on hover. */
export default function FollowButton({ user, className }: { user: UserSummary; className?: string }) {
  const viewer = usePage<SharedProps>().props.auth.user
  const follow = useSocialToggle({ url: `/users/${user.id}/follow`, kind: 'user', id: user.id, field: 'following', active: user.following, count: user.followerCount, countField: 'followerCount' })
  if (!viewer || viewer.id === user.id) return null
  return (
    <Button
      type="button" size="sm" variant={follow.active ? 'outline' : 'default'} disabled={follow.pending}
      aria-pressed={follow.active} onClick={follow.toggle}
      className={cn(
        'group/follow h-9 min-w-24 rounded-full px-4',
        follow.active && 'hover:border-destructive/40 hover:bg-destructive/10 hover:text-destructive',
        className,
      )}
    >
      {follow.active ? (
        <>
          <span className="group-hover/follow:hidden">Following</span>
          <span className="hidden group-hover/follow:inline">Unfollow</span>
        </>
      ) : 'Follow'}
    </Button>
  )
}
