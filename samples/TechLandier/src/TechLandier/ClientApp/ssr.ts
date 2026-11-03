import { createInertiaApp } from '@inertiajs/vue3'
import { layout, title } from './setup'
import { signalr } from './lib/inertia-signalr'

// @inertiajs/vite wraps this call in the SSR server bootstrap (createServer + renderToString).
// SSR pages subscribe to nothing (server-side), so the transport never loads during a render.
createInertiaApp({ layout, title, live: signalr() })
