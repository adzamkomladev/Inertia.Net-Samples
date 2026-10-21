import { usePage } from '@inertiajs/svelte'
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
  $effect(() => {
    const theme = (page.props.auth as { user: { theme?: Theme } | null } | undefined)?.user?.theme
    if (!theme) return
    applyTheme(theme)
    if (theme !== 'system' || typeof window === 'undefined') return
    const media = window.matchMedia('(prefers-color-scheme: dark)')
    const onChange = () => applyTheme('system')
    media.addEventListener('change', onChange)
    return () => media.removeEventListener('change', onChange)
  })
}
