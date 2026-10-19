import fs from 'node:fs'
import path from 'node:path'
import inertia from '@inertiajs/vite'
import tailwindcss from '@tailwindcss/vite'
import react from '@vitejs/plugin-react'
import { defineConfig, type Plugin } from 'vite'

// Inertia.Net finds the dev server through a "hot file" (wwwroot/hot) holding its origin.
// @inertiajs/vite does not write one, so this writes it while the dev server runs.
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
      for (const signal of ['SIGINT', 'SIGTERM'] as const) {
        process.once(signal, () => (remove(), process.exit()))
      }
      process.once('exit', remove)
    },
  }
}

export default defineConfig(({ command, isSsrBuild }) => ({
  // Dev: assets come from the Vite origin; build: from the app's /build/ path.
  base: command === 'build' ? '/build/' : '/',
  publicDir: false,
  plugins: [
    react(),
    tailwindcss(),
    // SSR server listens on 127.0.0.1:13714, the Inertia.Net default SSR address.
    inertia({ ssr: { entry: 'ClientApp/ssr.tsx', host: '127.0.0.1', port: 13714, sourcemap: false } }),
    hotFile(),
  ],
  resolve: { alias: { '@': path.resolve(import.meta.dirname, 'ClientApp') } },
  server: { port: 5173, strictPort: true, cors: true },
  // Bundle production SSR; let Bun load CommonJS dependencies natively during development.
  ssr: { noExternal: command === 'build' ? true : undefined },
  build: isSsrBuild
    ? { outDir: 'ssr', emptyOutDir: true }
    : {
        outDir: 'wwwroot/build',
        emptyOutDir: true,
        manifest: true,
        rolldownOptions: { input: 'ClientApp/app.tsx' },
      },
}))
