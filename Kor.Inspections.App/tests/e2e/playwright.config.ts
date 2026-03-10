import "dotenv/config";
import path from "path";
import { defineConfig, devices } from "@playwright/test";

// Absolute path keeps config and auth.ts in agreement regardless of CWD.
const STORAGE_STATE = path.join(__dirname, "storageState.json");

export default defineConfig({
  testDir: "./specs",
  globalSetup: "./setup/auth.ts",
  timeout: 30000,
  expect: {
    timeout: 10000
  },
  use: {
    baseURL: process.env.BASE_URL || "https://localhost:7074",
    storageState: STORAGE_STATE,
    ignoreHTTPSErrors: true,
    trace: "on-first-retry"
  },
  projects: [
    {
      name: "mobile-safari",
      use: {
        ...devices["iPhone 13"],
        storageState: STORAGE_STATE
      }
    }
  ]
});
