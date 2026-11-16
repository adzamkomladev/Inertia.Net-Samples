import type { Page, VisitOptions } from '@inertiajs/core'
import AppLayout from '@/layouts/AppLayout'
import AuthLayout from '@/layouts/AuthLayout'

/**
 * Persistent layouts: the header, tab bar, toaster and realtime connection mount once and survive navigation;
 * only the page below them swaps. Pages pass their title and width through `Page.layout = props => ({ ... })`.
 */
export function layout(name: string) {
  return name.startsWith('Auth/') ? AuthLayout : AppLayout
}

/** One title format for client and server renders, so shared links and search results carry the page name. */
export function title(pageTitle: string, page?: Page) {
  const appName = (page?.props as { appName?: string } | undefined)?.appName ?? 'Inkwell'
  return pageTitle ? `${pageTitle} · ${appName}` : appName
}

const reducedMotion = () => typeof window !== 'undefined' && window.matchMedia('(prefers-reduced-motion: reduce)').matches

/**
 * Full page visits cross-fade with the View Transitions API (the header and tab bar stay put). Partial reloads,
 * infinite scroll, filters and form posts keep their in-place behaviour, and reduced motion opts out.
 */
function visitOptions(_href: string, options: VisitOptions): Partial<VisitOptions> {
  const fullVisit = (options.method ?? 'get') === 'get' && !options.only?.length && !options.except?.length
    && !options.preserveState && !options.async
  return fullVisit && !reducedMotion() ? { viewTransition: true } : {}
}

export const defaults = {
  visitOptions,
  // Hover prefetches stay warm long enough to cover a reader's hesitation before the click.
  prefetch: { cacheFor: '30s' },
}
