import { createInertiaApp } from '@inertiajs/react'
import type { PageProps } from '@inertiajs/core'
import createServer from '@inertiajs/react/server'
import { renderToString } from 'react-dom/server'
import { signalr } from '@/lib/inertia-signalr'
import { layout, title } from '@/setup'

// Bootstrapped here rather than by @inertiajs/vite so the port is read at runtime: Inertia.Net's managed process
// passes INERTIA_SSR_PORT (Inertia:SsrBasePort), letting the sample run beside other apps on the default 13714.
const renderPromise = createInertiaApp<PageProps>({ layout, title, live: signalr() })
renderPromise.catch(console.error)

const port = Number(process.env.INERTIA_SSR_PORT ?? 13714)
if (!Number.isInteger(port) || port < 1 || port > 65535) throw new Error(`Invalid INERTIA_SSR_PORT: ${process.env.INERTIA_SSR_PORT}`)

createServer(async (page) => {
  const render = await renderPromise
  if (typeof render !== 'function') throw new Error('Inertia did not initialize an SSR renderer')
  return render(page, renderToString)
}, { host: '127.0.0.1', port })
