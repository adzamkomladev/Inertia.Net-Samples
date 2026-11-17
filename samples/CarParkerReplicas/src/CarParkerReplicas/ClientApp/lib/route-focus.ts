import { router } from '@inertiajs/svelte'

/**
 * After a visit to another path, move focus to the new page's heading (or <main>) and announce its title, so keyboard
 * and screen-reader users land on the new content instead of on a control that no longer exists. Partial reloads,
 * filters and paging on the same path keep focus where it is.
 */
export function routeFocus() {
  const announcer = document.createElement('p')
  announcer.className = 'sr-only'
  announcer.setAttribute('aria-live', 'polite')
  announcer.setAttribute('aria-atomic', 'true')
  document.body.appendChild(announcer)

  let path = location.pathname
  router.on('navigate', () => {
    if (location.pathname === path) return
    path = location.pathname
    // Wait for the page (and any skeleton swap) to render before choosing a target.
    requestAnimationFrame(() => requestAnimationFrame(() => {
      const main = document.getElementById('main') ?? document.querySelector<HTMLElement>('main')
      // A form page that autofocuses its first field (log in, reset password) keeps that: it is where the user starts.
      const auto = main?.querySelector<HTMLElement>('[autofocus]')
      const target = auto ?? main?.querySelector<HTMLElement>('h1') ?? main
      if (target) {
        if (target !== auto) {
          if (!target.hasAttribute('tabindex')) target.setAttribute('tabindex', '-1')
          target.classList.add('outline-none')
        }
        target.focus({ preventScroll: true })
      }
      announcer.textContent = document.title
    }))
  })
}
