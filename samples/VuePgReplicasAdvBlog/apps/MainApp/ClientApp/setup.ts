import type { Page } from '@inertiajs/core'
import AppLayout from '@/layouts/AppLayout.vue'
import AuthLayout from '@/layouts/AuthLayout.vue'

/** Shared by app.ts and ssr.ts. Pages under Auth/ get the centered auth layout, everything else the app shell. */
export const layout = (name: string, _page: Page) => (name.startsWith('Auth/') ? AuthLayout : AppLayout)

export const title = (title: string, page: Page) =>
  title ? `${title} · ${(page.props as { appName?: string }).appName ?? 'Inkwell'}` : ((page.props as { appName?: string }).appName ?? 'Inkwell')
