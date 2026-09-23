import react from '@vitejs/plugin-react'
import { defineConfig } from 'vitest/config'

export default defineConfig({
  plugins: [react()],
  test: {
    testTimeout: 15_000,
  },
  server: {
    proxy: {
      '/api': 'http://localhost:5046',
      '/hubs': {
        target: 'http://localhost:5046',
        ws: true,
      },
    },
  },
})
