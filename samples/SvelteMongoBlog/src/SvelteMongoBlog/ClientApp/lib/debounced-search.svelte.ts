import { router } from '@inertiajs/svelte'

/** Debounces typing and performs an Inertia GET, skipping the initial query value. */
export function useDebouncedSearch(query: () => string, href: string, delay = 400) {
  let initialized = false
  $effect(() => {
    const value = query()
    if (!initialized) {
      initialized = true
      return
    }
    const timer = setTimeout(() => {
      router.get(href, value.trim() ? { q: value.trim() } : {}, { replace: true, preserveState: true, preserveScroll: true })
    }, delay)
    return () => clearTimeout(timer)
  })
}
