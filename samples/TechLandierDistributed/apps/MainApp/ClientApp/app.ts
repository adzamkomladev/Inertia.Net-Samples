import './app.css'
import { createInertiaApp } from '@inertiajs/vue3'
import { layout, title } from './setup'
import { signalr } from './lib/inertia-signalr'

// @inertiajs/vite injects the page resolver (ClientApp/pages/**). Hydrates automatically when the page was server-rendered.
// Live props (notification bell, admin dashboards, order timelines) refresh over the live hub at /inertia-live, which
// HAProxy routes to the realtime tier; the autoloaded @microsoft/signalr chunk loads on the first subscription only.
createInertiaApp({ layout, title, progress: { color: 'var(--primary)' }, live: signalr() })
