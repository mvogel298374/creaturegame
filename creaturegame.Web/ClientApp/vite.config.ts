/// <reference types="vitest/config" />
import { defineConfig } from 'vitest/config';
import react from '@vitejs/plugin-react';

export default defineConfig({
  plugins: [react()],
  // Keep Vitest (unit) scoped to src/; Playwright owns e2e/.
  test: {
    include: ['src/**/*.test.ts'],
  },
  server: {
    port: 5173,
    proxy: {
      '/api': 'http://localhost:5100',
      '/hubs': {
        target: 'http://localhost:5100',
        ws: true,
      },
      '/sprites': 'http://localhost:5100',
      '/audio':   'http://localhost:5100',
    },
  },
});
