import { createInertiaApp } from '@inertiajs/vue3'
import type { PageProps } from '@inertiajs/core'
import createServer from '@inertiajs/vue3/server'
import { renderToString } from 'vue/server-renderer'
import { layout, title } from './setup'
import { signalr } from './lib/inertia-signalr'

// The live transport never connects during a render (pages subscribe to nothing server-side); it only
// gets wired the same way the browser bundle does, so the client and server run the same app shape.
const renderPromise = createInertiaApp<PageProps>({ layout, title, live: signalr() })
renderPromise.catch(console.error)

// One bundle serves every SSR replica: the host and port come from the environment at runtime.
const host = process.env.INERTIA_SSR_HOST ?? '127.0.0.1'
const port = Number(process.env.INERTIA_SSR_PORT ?? 13714)
if (!Number.isInteger(port) || port < 1 || port > 65535) throw new Error(`Invalid INERTIA_SSR_PORT: ${process.env.INERTIA_SSR_PORT}`)

createServer(async (page) => {
  const render = await renderPromise
  if (typeof render !== 'function') throw new Error('Inertia did not initialize an SSR renderer')
  return render(page, renderToString)
}, { host, port })
