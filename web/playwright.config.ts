import { defineConfig, devices } from '@playwright/test'

const API_PORT = 5097
const UI_PORT = 5174
const API_URL = `http://localhost:${API_PORT}`

// Uses the real UI and API, with only the Jev call stubbed. The servers are never reused, so a
// run can't attach to an API that's using the real classifier. If a port is in use, the run fails.
export default defineConfig({
  testDir: './e2e',
  forbidOnly: true,
  use: {
    baseURL: `http://localhost:${UI_PORT}`,
    trace: 'retain-on-failure',
  },
  projects: [{ name: 'chromium', use: { ...devices['Desktop Chrome'] } }],
  webServer: [
    {
      command: `dotnet run --project ../ColorApi --no-launch-profile --urls ${API_URL}`,
      port: API_PORT, // a TCP check, because GET on this POST-only API has no success response
      reuseExistingServer: false,
      timeout: 120_000,
      env: {
        ASPNETCORE_ENVIRONMENT: 'E2E',
        // Real variables win over .env, so the real key isn't loaded even if the stub fails.
        TYPESAFE_API_KEY: 'e2e-dummy-key',
      },
    },
    {
      command: `npm run dev -- --port ${UI_PORT} --strictPort`,
      url: `http://localhost:${UI_PORT}`,
      reuseExistingServer: false,
      env: { COLOR_API_URL: API_URL },
    },
  ],
})
