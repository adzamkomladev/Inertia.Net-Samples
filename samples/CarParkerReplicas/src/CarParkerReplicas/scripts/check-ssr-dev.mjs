import assert from 'node:assert/strict'
import { setTimeout } from 'node:timers/promises'
import { createServer, loadConfigFromFile } from 'vite'

// Exercise the real HTTP SSR endpoint without overwriting a running app's hot file or cache.
const { config } = await loadConfigFromFile({ command: 'serve', mode: 'development' })
const server = await createServer({
  ...config,
  configFile: false,
  cacheDir: '../../.playwright/ssr-cache',
  plugins: config.plugins.filter(plugin => plugin.name !== 'inertia-net-hot-file'),
  server: { ...config.server, host: '127.0.0.1', port: 0, strictPort: false, open: false },
})
try {
  await server.listen()
  const { port } = server.httpServer.address()
  const deadline = Date.now() + 60_000
  let result
  while (!result) {
    assert.ok(Date.now() < deadline, 'Development SSR did not finish warming up within 60 seconds')
    const response = await fetch(`http://127.0.0.1:${port}/__inertia_ssr`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({
        component: 'Home', url: '/', version: null,
        props: { appName: 'SSR check', auth: { user: null }, errors: {}, lots: [] },
        flash: {}, clearHistory: false, encryptHistory: false,
      }),
      signal: AbortSignal.timeout(Math.max(1, deadline - Date.now())),
    })
    assert.ok(response.ok, `SSR endpoint returned ${response.status}: ${await response.clone().text()}`)
    result = await response.json()
    if (!result) await setTimeout(250)
  }
  assert.ok(result.body.includes('Less circling.'))
  assert.ok(result.body.includes('No lots are open yet'))
  console.log('Development SSR endpoint rendered Home and realtime layout without browser globals')
} finally {
  await server.close()
}
