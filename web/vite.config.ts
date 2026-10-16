import { defineConfig } from 'vite';
import { svelte } from '@sveltejs/vite-plugin-svelte';

export default defineConfig({
  plugins: [svelte()],
  build: { outDir: 'dist', assetsDir: 'assets', emptyOutDir: true },
  server: {
    port: 5173,
    proxy: { '/v1': 'http://localhost:5099', '/pkhex': 'http://localhost:5099' },
  },
  test: { environment: 'jsdom', include: ['src/**/*.test.ts'] },
});
