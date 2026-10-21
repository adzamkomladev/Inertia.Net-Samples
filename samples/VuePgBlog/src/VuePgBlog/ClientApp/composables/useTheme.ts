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

const cookieTheme = (): Theme | null => {
  if (typeof document === 'undefined') return null
  const m = document.cookie.match(/(?:^|;\s*)theme=(light|dark|system)/)
  return (m?.[1] as Theme) ?? null
}

/**
 * Keeps <html class="dark"> in sync with the user's preference and the OS setting.
 * The theme cookie (written by applyTheme — header toggle, preferences page) wins over
 * the server claim, so quick toggles survive navigation without a round-trip.
 */
export function useTheme() {
  const page = usePage()
  const prefersDark = usePreferredDark()
  watch(
    [() => page.props.auth.user?.theme as Theme | undefined, prefersDark],
    ([theme]) => {
      const local = cookieTheme()
      applyTheme(local && local !== 'system' ? local : (theme ?? 'system'))
    },
    { immediate: true },
  )
}
