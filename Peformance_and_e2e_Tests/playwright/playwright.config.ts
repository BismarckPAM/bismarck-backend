import 'dotenv/config';
import { defineConfig, devices } from '@playwright/test';

export default defineConfig({
  testDir: './tests', fullyParallel: false, workers: 1, retries: 0,
  forbidOnly: Boolean(process.env.CI), timeout: 90_000,
  expect: { timeout: 15_000 },
  reporter: [
    ['list'], ['html', { open: 'never' }],
    ['junit', { outputFile: 'test-results/junit.xml' }],
    ['json', { outputFile: 'test-results/results.json' }],
  ],
  use: {
    ...devices['Desktop Chrome'], baseURL: process.env.FRONTEND_URL,
    serviceWorkers: 'block', ignoreHTTPSErrors: false,
    actionTimeout: 15_000, navigationTimeout: 30_000,
    trace: 'retain-on-failure', video: 'retain-on-failure', screenshot: 'only-on-failure',
  },
  projects: [
    { name: 'public', testMatch: '01-public.spec.ts' },
    { name: 'read', testMatch: '02-read.spec.ts' },
    { name: 'resilience', testMatch: '03-resilience.spec.ts' },
    { name: 'lifecycle', testMatch: '04-lifecycle.spec.ts' },
    { name: 'security', testMatch: '05-security.spec.ts' },
  ],
});
