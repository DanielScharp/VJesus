import react from '@vitejs/plugin-react'
import { defineConfig } from 'vite'

// base relativa: a UI é servida pelo WebView2 a partir de uma pasta local
export default defineConfig({
  base: './',
  plugins: [react()],
  server: { port: 5173, strictPort: true },
})
