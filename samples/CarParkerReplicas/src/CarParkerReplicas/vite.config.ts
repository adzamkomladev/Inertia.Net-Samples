import fs from 'node:fs'
import path from 'node:path'
import inertia from '@inertiajs/vite'
import { svelte } from '@sveltejs/vite-plugin-svelte'
import tailwindcss from '@tailwindcss/vite'
import { defineConfig, type Plugin } from 'vite'

// Inertia.Net finds the dev server through wwwroot/hot (same trick as laravel-vite-plugin).
function hotFile(): Plugin {
  const file = path.resolve(import.meta.dirname, 'wwwroot/hot')
  const remove = () => fs.rmSync(file, { force: true })
  return {
    name: 'inertia-net-hot-file',
    apply: 'serve',
    configureServer(server) {
      server.httpServer?.once('listening', () => {
        fs.mkdirSync(path.dirname(file), { recursive: true })
        fs.writeFileSync(file, server.resolvedUrls?.local[0]?.replace(/\/$/, '') ?? 'http://localhost:5173')
      })
      server.httpServer?.once('close', remove)
      for (const signal of ['SIGINT', 'SIGTERM'] as const) process.once(signal, () => (remove(), process.exit()))
      process.once('exit', remove)
    },
  }
}

// @inertiajs/vite bakes `ssr.port` into the bundle as a literal, and the Inertia SSR server reads no environment variable, so a
// bundle started by Inertia.Net's node process manager (which hands each instance its port in INERTIA_SSR_PORT) would
// always bind 13714. Make the baked port a runtime fallback instead.
function ssrPortFromEnv(): Plugin {
  return {
    name: 'ssr-port-from-env',
    apply: 'build',
    renderChunk(code, chunk) {
      if (!chunk.isEntry || !this.environment?.config.build.ssr) return null
      const baked = /("port":\s*)13714\b/
      if (!baked.test(code)) this.error('Could not find the SSR server port in the bundle; update ssrPortFromEnv for this @inertiajs/vite version.')
      return { code: code.replace(baked, '$1Number(process.env.INERTIA_SSR_PORT) || 13714'), map: null }
    },
  }
}

export default defineConfig(({ command, isSsrBuild }) => ({
  base: command === 'build' ? '/build/' : '/',
  publicDir: false,
  plugins: [svelte(), tailwindcss(), inertia({ ssr: { entry: 'ClientApp/ssr.ts', host: '127.0.0.1', port: 13714 } }), hotFile(), ssrPortFromEnv()],
  resolve: { alias: { '@': path.resolve(import.meta.dirname, 'ClientApp') } },
  server: { port: 5173, strictPort: true, cors: true },
  // Bundle deps into ssr/ssr.js so the runtime image needs only the bun binary, no node_modules.
  ssr: { noExternal: command === 'build' ? true : undefined },
  build: isSsrBuild
    ? { outDir: 'ssr', emptyOutDir: true }
    : { outDir: 'wwwroot/build', emptyOutDir: true, manifest: true, rolldownOptions: { input: 'ClientApp/app.ts' } },
}))
