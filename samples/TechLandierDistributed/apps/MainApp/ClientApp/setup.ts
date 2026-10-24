import type { Page } from '@inertiajs/core'
import StoreLayout from '@/layouts/StoreLayout.vue'
import AccountLayout from '@/layouts/AccountLayout.vue'
import AdminLayout from '@/layouts/AdminLayout.vue'
import AuthLayout from '@/layouts/AuthLayout.vue'

/**
 * Shared by app.ts and ssr.ts. The first path segment picks the shell:
 * /account → Account, /admin → Admin, Auth/* pages → Auth, everything else Store.
 */
export const layout = (name: string, page: Page) => {
  if (name.startsWith('Auth/')) return AuthLayout
  const segment = '/' + (page.url.split('?')[0]!.split('/').filter(Boolean)[0] ?? '')
  if (segment === '/account') return AccountLayout
  if (segment === '/admin') return AdminLayout
  return StoreLayout
}

export const title = (title: string, page: Page) =>
  title ? `${title} · ${(page.props as { appName?: string }).appName ?? 'TechLandier'}` : ((page.props as { appName?: string }).appName ?? 'TechLandier')
