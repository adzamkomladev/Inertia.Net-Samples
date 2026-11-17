import './app.css'
import { createApp } from 'vue'
import { createInertiaApp } from '@inertiajs/vue3'
import AppToaster from './components/AppToaster.vue'
import { layout, title } from './setup'
import { signalr } from './lib/inertia-signalr'

// @inertiajs/vite injects the page resolver (ClientApp/pages/**). Hydrates automatically when the page was server-rendered.
// Live props (notification bell, admin dashboards, order timelines) refresh over the live hub; the connection starts
// on the first page that declares a listener, so storefront pages without listeners never open one.
createInertiaApp({ layout, title, progress: { color: 'var(--primary)' }, live: signalr() })

// One Toaster for every layout, client only (toasts are never part of server-rendered HTML).
const toaster = document.createElement('div')
document.body.appendChild(toaster)
createApp(AppToaster).mount(toaster)
