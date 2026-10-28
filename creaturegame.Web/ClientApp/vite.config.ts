/// <reference types="vitest/config" />
import { defineConfig } from 'vitest/config';
import { createLogger } from 'vite';
import react from '@vitejs/plugin-react';

// The blip specs reset the hub socket on purpose (e2e/README.md → How it works); other proxy errors still print.
const logger = createLogger();
const logError = logger.error;
logger.error = (msg, options) => {
  if (msg.includes('ws proxy error') && (options?.error as NodeJS.ErrnoException | undefined)?.code === 'ECONNRESET') return;
  logError(msg, options);
};

export default defineConfig({
  customLogger: logger,
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
