import fs from 'node:fs'
import path from 'node:path'
import inertia from '@inertiajs/vite'
import tailwindcss from '@tailwindcss/vite'
import vue from '@vitejs/plugin-vue'
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

export default defineConfig(({ command, isSsrBuild }) => ({
  base: command === 'build' ? '/build/' : '/',
  publicDir: false,
  plugins: [vue(), tailwindcss(), inertia({ ssr: 'ClientApp/ssr.ts' }), hotFile()],
  resolve: { alias: { '@': path.resolve(import.meta.dirname, 'ClientApp') } },
  server: { port: 5173, strictPort: true, cors: true },
  // Bundle deps into ssr/ssr.js so the runtime image needs only the node binary, no node_modules.
  ssr: { noExternal: command === 'build' ? true : undefined },
  build: isSsrBuild
    ? { outDir: 'ssr', emptyOutDir: true }
    : { outDir: 'wwwroot/build', emptyOutDir: true, manifest: true, rolldownOptions: { input: 'ClientApp/app.ts' } },
}))
