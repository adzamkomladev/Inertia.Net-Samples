import './app.css'
import { mount } from 'svelte'
import { createInertiaApp } from '@inertiajs/svelte'
import AppToaster from './components/AppToaster.svelte'
import OfflineBanner from './components/OfflineBanner.svelte'
import { layout } from './setup'
import { signalr } from './lib/inertia-signalr'
import { routeFocus } from './lib/route-focus'

// @inertiajs/vite injects the page resolver (ClientApp/pages/**). Hydrates automatically when the page was server-rendered.
// Live props (staff consoles, notification badge, alerts page) refresh over the live hub; the connection starts on the
// first page that declares a listener. Storefront Scroll props stay on the raw /realtime pulses in lib/realtime.
createInertiaApp({ layout, progress: { color: 'var(--primary)' }, live: signalr() })

// One Toaster for every layout, client only (toasts are never part of server-rendered HTML).
const toaster = document.createElement('div')
document.body.appendChild(toaster)
mount(AppToaster, { target: toaster })

// One offline pill for every layout, client only; it shows when the browser or the heartbeat says the app is offline.
const offlineBanner = document.createElement('div')
document.body.appendChild(offlineBanner)
mount(OfflineBanner, { target: offlineBanner })
routeFocus()
