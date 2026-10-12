import './app.css'
import { createInertiaApp } from '@inertiajs/vue3'
import { layout, title } from './setup'

// @inertiajs/vite injects the page resolver (ClientApp/pages/**). Hydrates automatically when the page was server-rendered.
createInertiaApp({ layout, title, progress: { color: 'var(--primary)' } })
