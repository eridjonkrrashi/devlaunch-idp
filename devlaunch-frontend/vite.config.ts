import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'
import tailwindcss from '@tailwindcss/vite'

export default defineConfig({
  plugins: [react(), tailwindcss()],
  build: {
    outDir: '../DevLaunch.Api/wwwroot',
    emptyOutDir: true,
  },
  server: {
    proxy: {
      '/api': 'http://localhost:5293',
      '/health': 'http://localhost:5293',
      '/ready': 'http://localhost:5293',
      '/metrics': 'http://localhost:5293',
    },
  },
})
