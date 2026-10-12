import { computed, ref, watch } from 'vue'
import { router } from '@inertiajs/vue3'
import { useToggle } from '@vueuse/core'

type Entity = Record<string, unknown> & { id: string }
type Patch = (entity: Entity) => Entity

// The server answers a refused write (e.g. rate limited) with a redirect plus an error flash: that is not a success.
export const refused = (page: { flash?: unknown }) => (page.flash as { toast?: { type?: string } } | undefined)?.toast?.type === 'error'

const same = (a: unknown, b: unknown) => String(a) === String(b)
const patchList = (list: unknown, id: string, patch: Patch) =>
  Array.isArray(list) ? list.map((item: Entity) => same(item.id, id) ? patch(item) : item) : list

/** Rewrites one post or user wherever the current page holds it, without asking the server for the list again. */
export function patchEntity(kind: 'post' | 'user', id: string, patch: Patch, extra?: Record<string, unknown>, drop?: (props: Record<string, any>) => boolean) {
  const list = `${kind}s`
  router.replace({
    preserveState: true,
    preserveScroll: true,
    props: (old: Record<string, any>) => {
      const next: Record<string, any> = { ...old }
      for (const [key, value] of Object.entries(extra ?? {})) if (key in old) next[key] = value
      if (old[list]?.data) {
        const data = drop?.(old) ? old[list].data.filter((item: Entity) => !same(item.id, id)) : patchList(old[list].data, id, patch)
        next[list] = { ...old[list], data }
      }
      if (old[kind] && same(old[kind].id, id)) next[kind] = patch(old[kind])
      return next
    },
  })
}

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
  const shown = ref<{ active: boolean; count: number } | null>(null)
  const [pending, togglePending] = useToggle(false)
  // Fresh server props (patch or live counter event) replace the local value.
  watch(() => [active(), count?.() ?? 0], () => { shown.value = null })

  const toggle = () => {
    if (pending.value) return
    togglePending(true)
    // Toggle from what the user sees: it stays correct even if the entity is not in this page's props.
    const current = shown.value?.active ?? active()
    const next = !current
    const base = count?.() ?? 0
    let confirmed = false
    shown.value = { active: next, count: Math.max(0, (shown.value?.count ?? base) + (next ? 1 : -1)) }
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
        patchEntity(kind, id(), entity => ({
          ...entity,
          [field]: next,
          ...(countField && Number(entity[countField] ?? 0) === base ? { [countField]: Math.max(0, base + (next ? 1 : -1)) } : {}),
        }), field === 'liked' ? { liked: next } : undefined, props => field === 'bookmarked' && !next && props.mode === 'bookmarks')
        // Prefetched pages still hold the old relationship.
        router.flushAll()
      },
      onFinish: () => {
        togglePending(false)
        if (!confirmed) shown.value = null
      },
    }
    if (current) router.delete(url(), options)
    else router.put(url(), {}, options)
  }

  return {
    active: computed(() => shown.value?.active ?? active()),
    count: computed(() => shown.value?.count ?? count?.() ?? 0),
    pending,
    toggle,
  }
}

/** Fire-and-forget PUT/DELETE (archive/restore) with a busy flag and no optimistic value. */
export function useSimpleToggle(url: () => string, active: () => boolean) {
  const [pending, togglePending] = useToggle(false)
  const toggle = () => {
    if (pending.value) return
    togglePending(true)
    const options = { preserveScroll: true, onFinish: () => togglePending(false) }
    if (active()) router.delete(url(), options)
    else router.put(url(), {}, options)
  }
  return { pending, toggle }
}
