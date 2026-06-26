import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'

// https://vite.dev/config/
export default defineConfig({
  plugins: [react()],
  server: {
    // Proxy API calls to the .NET backend so the browser can use same-origin '/api'.
    proxy: {
      '/api': {
        target: 'http://localhost:5099',
        changeOrigin: true,
      },
    },
  },
})
