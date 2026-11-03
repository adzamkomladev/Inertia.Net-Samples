import { createInertiaApp } from '@inertiajs/svelte'
import { layout } from './setup'
import { signalr } from './lib/inertia-signalr'

// @inertiajs/vite wraps this call in the SSR server bootstrap (createServer + svelte/server render).
createInertiaApp({ layout, live: signalr() })
