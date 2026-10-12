import { createInertiaApp } from '@inertiajs/vue3'
import { layout, title } from './setup'

// @inertiajs/vite wraps this call in the SSR server bootstrap (createServer + renderToString).
createInertiaApp({ layout, title })
