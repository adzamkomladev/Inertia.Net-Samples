import { router } from '@inertiajs/svelte'
import { patchEntityProps, refused, socialPatch } from './social-toggle'

interface SocialToggleOptions {
  url: () => string
  kind?: 'post' | 'user'
  id: () => string
  field: string
  active: () => boolean
  count?: () => number | undefined
  countField?: string
}

/**
 * Toggles a relationship (like, repost, bookmark, follow) with a PUT/DELETE.
 * The visit only reloads the tiny `auth` prop, so feeds and infinite-scroll pages keep their items and scroll
 * position. The control flips immediately; after the server confirms, the entity is patched in place. A rejected
 * or cancelled visit drops the local value, so the server props remain the only source of truth.
 * Live counter events that arrive during the visit are applied after it finishes and win over the local count.
 */
export function useSocialToggle({ url, kind = 'post', id, field, active, count, countField }: SocialToggleOptions) {
  let shown = $state<{ active: boolean; count: number } | null>(null)
  let pending = $state(false)

  // Fresh server props (patch or live counter event) replace the local value.
  $effect(() => {
    active()
    count?.()
    shown = null
  })

  const toggle = () => {
    if (pending) return
    pending = true
    // Toggle from what the user sees: it stays correct even if the entity is not in this page's props.
    const current = shown?.active ?? active()
    const next = !current
    const base = count?.() ?? 0
    let confirmed = false
    shown = { active: next, count: Math.max(0, (shown?.count ?? base) + (next ? 1 : -1)) }
    const options = {
      only: ['auth'],
      // Non-interruptible: a second toggle elsewhere must not cancel this one.
      async: true,
      preserveScroll: true,
      preserveState: true,
      onSuccess: (page: { flash?: unknown }) => {
        if (refused(page)) return
        confirmed = true
        // A live counter event may already have counted this change; only adjust a count that is still the baseline.
        const patch = socialPatch({ field, next, base, countField })
        router.replace({
          preserveState: true,
          preserveScroll: true,
          props: (old: Record<string, any>) => patchEntityProps(old, {
            kind,
            id: id(),
            patch,
            extra: field === 'liked' ? { liked: next } : undefined,
            drop: props => field === 'bookmarked' && !next && props.mode === 'bookmarks',
          }),
        })
        // Prefetched pages still hold the old relationship.
        router.flushAll()
      },
      onFinish: () => {
        pending = false
        if (!confirmed) shown = null
      },
    }
    if (current) router.delete(url(), options)
    else router.put(url(), {}, options)
  }

  return {
    get active() { return shown?.active ?? active() },
    get count() { return shown?.count ?? count?.() ?? 0 },
    get pending() { return pending },
    toggle,
  }
}

/** Fire-and-forget PUT/DELETE (archive/restore) with a busy flag and no optimistic value. */
export function useSimpleToggle(url: () => string, active: () => boolean) {
  let pending = $state(false)
  const toggle = () => {
    if (pending) return
    pending = true
    const options = { preserveScroll: true, onFinish: () => (pending = false) }
    if (active()) router.delete(url(), options)
    else router.put(url(), {}, options)
  }
  return { get pending() { return pending }, toggle }
}
