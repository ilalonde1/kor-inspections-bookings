import { defineConfig, devices } from "@playwright/test";

export default defineConfig({
  testDir: "./specs",
  globalSetup: "./setup/auth.ts",
  timeout: 30000,
  expect: {
    timeout: 10000
  },
  use: {
    baseURL: process.env.BASE_URL || "http://localhost:5000",
    storageState: "storageState.json",
    ignoreHTTPSErrors: true,
    trace: "on-first-retry"
  },
  projects: [
    {
      name: "mobile-safari",
      use: {
        ...devices["iPhone 13"]
      }
    }
  ]
});
