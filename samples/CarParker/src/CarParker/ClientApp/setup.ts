import type { Page } from '@inertiajs/core'
import AuthLayout from '@/layouts/AuthLayout.svelte'
import StoreLayout from '@/layouts/StoreLayout.svelte'
import AccountLayout from '@/layouts/AccountLayout.svelte'
import GuardLayout from '@/layouts/GuardLayout.svelte'
import AdminLayout from '@/layouts/AdminLayout.svelte'

/**
 * Shared layout resolver: the route's first segment picks the console shell.
 * Auth/* pages get the centered auth layout, everything else the storefront.
 */
export const layout = (name: string, _page: Page) => {
  if (name.startsWith('Auth/')) return AuthLayout
  if (name.startsWith('account/')) return AccountLayout
  if (name.startsWith('guard/')) return GuardLayout
  if (name.startsWith('admin/')) return AdminLayout
  return StoreLayout
}
