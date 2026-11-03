import './app.css'
import { createInertiaApp } from '@inertiajs/vue3'
import { layout, title } from './setup'
import { signalr } from './lib/inertia-signalr'

// @inertiajs/vite injects the page resolver (ClientApp/pages/**). Hydrates automatically when the page was server-rendered.
// Live props (notification bell, admin dashboards, order timelines) refresh over the live hub; the connection starts
// on the first page that declares a listener, so storefront pages without listeners never open one.
createInertiaApp({ layout, title, progress: { color: 'var(--primary)' }, live: signalr() })
