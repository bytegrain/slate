import { defineConfig, devices } from '@playwright/test';

export default defineConfig({
  testDir: './tests/e2e',
  fullyParallel: true,
  retries: process.env.CI ? 2 : 0,
  reporter: process.env.CI ? 'github' : 'list',
  use: {
    ...devices['Desktop Chrome'],
    browserName: 'chromium',
    headless: true,
    trace: 'retain-on-failure',
  },
  webServer: [
    {
      command: 'npm run dev --workspace @bytegrain/slate-web -- --host 127.0.0.1',
      url: 'http://127.0.0.1:5173',
      reuseExistingServer: !process.env.CI,
      timeout: 60_000,
    },
    {
      command: 'dotnet run --project demos/Slate.Blazor.Demo --urls http://127.0.0.1:5091',
      url: 'http://127.0.0.1:5091',
      reuseExistingServer: !process.env.CI,
      timeout: 120_000,
    },
  ],
});
