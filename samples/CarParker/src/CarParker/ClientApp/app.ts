import './app.css'
import { createInertiaApp } from '@inertiajs/svelte'
import { layout } from './setup'

// @inertiajs/vite injects the page resolver (ClientApp/pages/**). Hydrates automatically when the page was server-rendered.
createInertiaApp({ layout, progress: { color: 'var(--primary)' } })
