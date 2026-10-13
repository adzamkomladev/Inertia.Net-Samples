import assert from 'node:assert/strict'
import { createServer } from 'vite'

// Exercise the same module runner as bun run dev without opening ports.
const server = await createServer({ server: { middlewareMode: true }, appType: 'custom' })
try {
  const { default: render } = await server.ssrLoadModule('ClientApp/ssr.tsx')
  const result = await render({
    component: 'Posts/Index', url: '/posts', version: null,
    props: { appName: 'SSR check', auth: { user: null }, errors: {}, posts: { data: [] }, q: '' },
    flash: {}, clearHistory: false, encryptHistory: false,
  })
  assert.ok(result.body.includes('No posts yet'))
  console.log('Development SSR rendered Posts/Index successfully')
} finally {
  await server.close()
}
