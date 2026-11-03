import './app.css'
import { createInertiaApp } from '@inertiajs/react'
import { signalr } from '@/lib/inertia-signalr'

// @inertiajs/vite injects the page resolver (ClientApp/pages/**) and hydrates when the page was server-rendered.
// Live props (notifications badge, analytics dashboards) refresh over the live hub; the connection starts on the
// first page that declares a listener, so visitors of pages without live props never open it.
createInertiaApp({ live: signalr() })
