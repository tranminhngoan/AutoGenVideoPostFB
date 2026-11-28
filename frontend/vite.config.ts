import { resolve } from 'node:path';
import { defineConfig } from 'vite';
import react from '@vitejs/plugin-react';

export default defineConfig({
  plugins: [react()],
  build: {
    rollupOptions: {
      input: {
        // Admin app (password gate)
        main: resolve(__dirname, 'index.html'),
        // PUBLIC landing page /cheo-uy-tinh — own bundle, no gate, no login
        cheoUyTinh: resolve(__dirname, 'cheo-uy-tinh/index.html'),
      },
    },
  },
  server: {
    port: 5173,
    proxy: {
      '/api': {
        target: 'http://localhost:5000',
        changeOrigin: true,
        secure: false
      },
      '/health': {
        target: 'http://localhost:5000',
        changeOrigin: true,
        secure: false
      }
    }
  }
});
