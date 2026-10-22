import { router } from '@inertiajs/react'
import { useEffect, useRef } from 'react'
import { useDebouncedValue } from './use-debounced-value'

/** Debounces typing and performs an Inertia GET, skipping the initial mount. */
export function useDebouncedSearch(query: string, href: string, delay = 400) {
  const debounced = useDebouncedValue(query.trim(), delay)
  const previous = useRef(query.trim())
  useEffect(() => {
    if (previous.current === debounced) return
    previous.current = debounced
    router.get(href, debounced ? { q: debounced } : {}, { replace: true, preserveState: true, preserveScroll: true })
  }, [debounced, href])
}
