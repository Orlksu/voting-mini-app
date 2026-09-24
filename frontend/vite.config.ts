import { readFileSync } from 'node:fs'
import { resolve } from 'node:path'
import react from '@vitejs/plugin-react'
import { defineConfig, type Plugin } from 'vite'

function mapLibreWorkerDependencies(): Plugin {
  return {
    name: 'maplibre-worker-dependencies',
    generateBundle() {
      this.emitFile({
        type: 'asset',
        fileName: 'assets/maplibre-gl-shared.mjs',
        source: readFileSync(
          resolve(
            process.cwd(),
            'node_modules/maplibre-gl/dist/maplibre-gl-shared.mjs',
          ),
        ),
      })
    },
  }
}

// https://vite.dev/config/
export default defineConfig({
  plugins: [react(), mapLibreWorkerDependencies()],
})
