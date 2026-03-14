import { defineConfig } from 'vite';
import react from '@vitejs/plugin-react';

export default defineConfig({
  base: '/app/',
  plugins: [react()],
  build: {
    outDir: '../wwwroot/app',
    emptyOutDir: true
  },
  server: {
    port: 5173,
    strictPort: true
  },
  test: {
    environment: 'jsdom',
    include: ['src/**/*.test.ts', 'src/**/*.test.tsx']
  }
});