import { defineConfig, devices } from '@playwright/test';

/**
 * E2E tests run against the Vite dev server (:5173), which proxies /api, /hubs (ws),
 * /sprites, and /audio to the .NET backend (:5100).
 *
 * ⚠️  The .NET backend MUST be running — start the full stack with `./dev.ps1`
 * (or `dotnet run --project creaturegame.Web`) before running these tests. The
 * webServer block below only manages the Vite frontend; it reuses an already-running
 * dev server if one is up.
 *
 * Battles are stateful (one in-flight battle per SignalR connection), so tests run
 * serially with a single worker.
 */
export default defineConfig({
  testDir: './e2e',
  fullyParallel: false,
  workers: 1,
  retries: 0,
  timeout: 30_000,
  expect: { timeout: 10_000 },
  reporter: 'list',
  use: {
    baseURL: 'http://localhost:5173',
    // Bound individual actions so a click on a control that's disabled by battle
    // end (e.g. FIGHT once a winner is decided) fails fast instead of hanging.
    actionTimeout: 7_000,
    trace: 'retain-on-failure',
    video: 'retain-on-failure',
    screenshot: 'only-on-failure',
  },
  projects: [
    { name: 'chromium', use: { ...devices['Desktop Chrome'] } },
  ],
  webServer: {
    command: 'npm run dev',
    url: 'http://localhost:5173',
    reuseExistingServer: true,
    timeout: 120_000,
  },
});
