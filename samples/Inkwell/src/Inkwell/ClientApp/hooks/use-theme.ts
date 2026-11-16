import { useEffect } from 'react'
import type { Theme } from '@/types/shared'

export function applyTheme(theme: Theme) {
  const dark = theme === 'dark' || (theme === 'system' && window.matchMedia('(prefers-color-scheme: dark)').matches)
  document.documentElement.classList.toggle('dark', dark)
}

/** Keeps <html class="dark"> in sync with the signed-in user's preference, and with the OS while it is "system". */
export function useTheme(theme: Theme) {
  useEffect(() => {
    applyTheme(theme)
    if (theme !== 'system') return
    const query = window.matchMedia('(prefers-color-scheme: dark)')
    const follow = () => applyTheme('system')
    query.addEventListener('change', follow)
    return () => query.removeEventListener('change', follow)
  }, [theme])
}
