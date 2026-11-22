import './app.css'
import { createApp } from 'vue'
import { createInertiaApp } from '@inertiajs/vue3'
import AppToaster from './components/AppToaster.vue'
import OfflineBanner from './components/OfflineBanner.vue'
import { layout, title } from './setup'
import { signalr } from './lib/inertia-signalr'

// @inertiajs/vite injects the page resolver (ClientApp/pages/**). Hydrates automatically when the page was server-rendered.
// Live props (notification bell, admin dashboards, order timelines) refresh over the live hub at /inertia-live, which
// HAProxy routes to the realtime tier; the autoloaded @microsoft/signalr chunk loads on the first subscription only.
createInertiaApp({ layout, title, progress: { color: 'var(--primary)' }, live: signalr() })

// One Toaster for every layout, client only (toasts are never part of server-rendered HTML).
const toaster = document.createElement('div')
document.body.appendChild(toaster)
createApp(AppToaster).mount(toaster)

// One offline pill for every layout, client only; it shows when the browser or the heartbeat says the app is offline.
const offline = document.createElement('div')
document.body.appendChild(offline)
createApp(OfflineBanner).mount(offline)
