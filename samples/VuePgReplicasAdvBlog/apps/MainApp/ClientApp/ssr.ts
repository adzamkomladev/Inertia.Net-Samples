import { createInertiaApp } from '@inertiajs/vue3'
import type { PageProps } from '@inertiajs/core'
import createServer from '@inertiajs/vue3/server'
import { renderToString } from 'vue/server-renderer'
import { layout, title } from './setup'

const renderPromise = createInertiaApp<PageProps>({ layout, title })
renderPromise.catch(console.error)

// Inertia.Net supplies this from the Node process base port; read it at runtime so one bundle serves every replica.
createServer(async (page) => {
  const render = await renderPromise
  if (typeof render !== 'function') throw new Error('Inertia did not initialize an SSR renderer')
  return render(page, renderToString)
}, { port: Number(process.env.INERTIA_SSR_PORT ?? 13714), host: '127.0.0.1' })
