import type { Page } from '@inertiajs/core'
import AppLayout from '@/layouts/AppLayout.svelte'
import AuthLayout from '@/layouts/AuthLayout.svelte'

/** Shared by app.ts and ssr.ts. Pages under Auth/ get the centered auth layout, everything else the app shell. */
export const layout = (name: string, _page: Page) => (name.startsWith('Auth/') ? AuthLayout : AppLayout)
