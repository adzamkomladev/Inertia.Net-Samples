import { watch, type Ref } from 'vue'
import { router } from '@inertiajs/vue3'
import { useDebounceFn } from '@vueuse/core'

/** Debounces typing and performs an Inertia GET, skipping the initial query value. */
export function useDebouncedSearch(query: Ref<string>, href: string, delay = 400) {
  const run = useDebounceFn((value: string) => {
    router.get(href, value.trim() ? { q: value.trim() } : {}, { replace: true, preserveState: true, preserveScroll: true })
  }, delay)
  watch(query, (value) => {
    run(value)
  })
}
