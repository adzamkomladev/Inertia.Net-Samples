import { createInertiaApp } from '@inertiajs/react'
import { signalr } from '@/lib/inertia-signalr'

// @inertiajs/vite wraps this call with the SSR server bootstrap (127.0.0.1:13714) in `vite build --ssr`.
createInertiaApp({ live: signalr() })
