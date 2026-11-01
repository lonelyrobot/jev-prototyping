import react from '@vitejs/plugin-react'
import { defineConfig } from 'vitest/config'

export default defineConfig({
  plugins: [react()],
  server: {
    // Same origin for the browser, so the API doesn't need CORS.
    proxy: {
      '/color': process.env.COLOR_API_URL ?? 'http://localhost:5000',
    },
  },
  test: {
    environment: 'jsdom',
    include: ['src/**/*.test.tsx'], // e2e/ belongs to Playwright
  },
})
