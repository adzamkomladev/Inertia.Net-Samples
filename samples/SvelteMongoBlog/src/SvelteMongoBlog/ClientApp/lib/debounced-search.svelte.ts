import { router } from '@inertiajs/svelte'

type FilterParams = { q?: string; type?: string; maxPrice?: number | string; sort?: string } & Record<string, unknown>

/** Debounces typing and performs an Inertia GET with the given filter params, skipping the initial value. */
export function useDebouncedSearch(query: () => string, href: string, build?: () => FilterParams, delay = 400) {
  let initialized = false
  $effect(() => {
    const value = query()
    if (!initialized) {
      initialized = true
      return
    }
    const timer = setTimeout(() => {
      const extra = build?.() ?? {}
      const params: FilterParams = { ...extra }
      if (value.trim()) params.q = value.trim()
      // Drop empty keys so the URL stays clean across filter changes.
      for (const key of Object.keys(params)) {
        const v = params[key]
        if (v === '' || v === undefined || v === null) delete params[key as keyof FilterParams]
      }
      router.get(href, params, { replace: true, preserveState: true, preserveScroll: true })
    }, delay)
    return () => clearTimeout(timer)
  })
}
