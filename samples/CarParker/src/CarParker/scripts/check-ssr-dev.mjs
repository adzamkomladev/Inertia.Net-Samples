import assert from 'node:assert/strict'
import { createServer } from 'vite'

// Exercise the same module runner as bun run dev without opening ports.
const server = await createServer({ server: { middlewareMode: true }, appType: 'custom' })
try {
  const { default: render } = await server.ssrLoadModule('ClientApp/ssr.ts')
  const result = await render({
    component: 'Home', url: '/', version: null,
    props: { appName: 'SSR check', auth: { user: null }, errors: {}, lots: [] },
    flash: {}, clearHistory: false, encryptHistory: false,
  })
  assert.ok(result.body.includes('Reserve a parking bay before you arrive.'))
  assert.ok(result.body.includes('No lots are open yet'))
  console.log('Development SSR rendered Home and realtime layout without browser globals')
} finally {
  await server.close()
}
