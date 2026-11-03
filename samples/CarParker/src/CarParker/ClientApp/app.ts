import './app.css'
import { createInertiaApp } from '@inertiajs/svelte'
import { layout } from './setup'
import { signalr } from './lib/inertia-signalr'

// @inertiajs/vite injects the page resolver (ClientApp/pages/**). Hydrates automatically when the page was server-rendered.
// Live props (staff consoles, notification badge, alerts page) refresh over the live hub; the connection starts on the
// first page that declares a listener. Storefront Scroll props stay on the raw /realtime pulses in lib/realtime.
createInertiaApp({ layout, progress: { color: 'var(--primary)' }, live: signalr() })
