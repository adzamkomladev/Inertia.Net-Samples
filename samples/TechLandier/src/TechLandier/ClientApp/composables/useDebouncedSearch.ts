import type { Ref } from 'vue'
import { router } from '@inertiajs/vue3'
import { watchDebounced } from '@vueuse/core'

/** Debounces typing and performs an Inertia GET, skipping the initial query value. */
export function useDebouncedSearch(query: Ref<string>, href: string, delay = 400) {
  watchDebounced(query, (value) => {
    router.get(href, value.trim() ? { q: value.trim() } : {}, { replace: true, preserveState: true, preserveScroll: true })
  }, { debounce: delay })
}
