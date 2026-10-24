import { createInertiaApp } from '@inertiajs/svelte'
import { layout } from './setup'

// @inertiajs/vite wraps this call in the SSR server bootstrap (createServer + svelte/server render).
createInertiaApp({ layout })
