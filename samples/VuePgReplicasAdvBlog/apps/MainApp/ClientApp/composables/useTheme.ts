import { watch } from 'vue'
import { usePage } from '@inertiajs/vue3'
import { usePreferredDark } from '@vueuse/core'
import type { Theme } from '@/types'

export function applyTheme(theme: Theme) {
  if (typeof document === 'undefined') return
  const dark = theme === 'dark' || (theme === 'system' && window.matchMedia('(prefers-color-scheme: dark)').matches)
  document.documentElement.classList.toggle('dark', dark)
  document.cookie = `theme=${theme}; path=/; max-age=31536000; samesite=lax`
}

/** Keeps <html class="dark"> in sync with the user's preference and the OS setting. */
export function useTheme() {
  const page = usePage()
  const prefersDark = usePreferredDark()
  watch(
    [() => page.props.auth.user?.theme as Theme | undefined, prefersDark],
    ([theme]) => theme && applyTheme(theme),
    { immediate: true },
  )
}
