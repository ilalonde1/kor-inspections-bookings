import { defineConfig, devices } from "@playwright/test";

export default defineConfig({
  testDir: "./specs",
  timeout: 30000,
  expect: {
    timeout: 10000
  },
  use: {
    baseURL: process.env.BASE_URL || "http://localhost:5000",
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
