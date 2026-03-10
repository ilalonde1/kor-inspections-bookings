import "dotenv/config";
import path from "path";
import { chromium, type FullConfig } from "@playwright/test";

// Absolute path so the file lands in tests/e2e/ regardless of CWD.
const STORAGE_STATE = path.join(__dirname, "..", "storageState.json");

async function globalSetup(config: FullConfig) {
  const baseUrl = (process.env.BASE_URL || config.projects[0]?.use?.baseURL || "https://localhost:7074").replace(/\/$/, "");
  const email = process.env.TEST_ADMIN_EMAIL;
  const password = process.env.TEST_ADMIN_PASSWORD;

  if (!email || !password) {
    throw new Error("TEST_ADMIN_EMAIL and TEST_ADMIN_PASSWORD must be set for Playwright auth setup.");
  }

  const browser = await chromium.launch();
  // ignoreHTTPSErrors is required so the post-Microsoft redirect back to the
  // local dev server (self-signed cert) is not blocked by Chromium.
  const context = await browser.newContext({ ignoreHTTPSErrors: true });
  const page = await context.newPage();

  try {
    await page.goto(`${baseUrl}/admin`, { waitUntil: "domcontentloaded" });

    // Microsoft login — email step
    await page.waitForSelector("#i0116", { state: "visible", timeout: 15000 });
    await page.fill("#i0116", email);
    await page.click("#idSIButton9");

    // Microsoft login — password step
    await page.waitForSelector("#i0118", { state: "visible", timeout: 15000 });
    await page.fill("#i0118", password);
    await page.click("#idSIButton9");

    // "Stay signed in?" prompt — optional, click Yes if it appears
    try {
      await page.waitForSelector("#idSIButton9", { timeout: 5000 });
      await page.click("#idSIButton9");
    } catch {
      // Prompt did not appear — continue
    }

    // Wait until the full OIDC redirect chain has completed and the browser
    // has landed back on the local app.  This is the point where ASP.NET Core
    // sets the real session cookie (.AspNetCore.Cookies).  Saving state before
    // this resolves captures only Microsoft-side cookies, not the app session.
    try {
      await page.waitForURL(`${baseUrl}/**`, { timeout: 30000 });
    } catch (err) {
      // Capture the page state so we can see what Microsoft screen is blocking
      // the redirect (MFA prompt, consent page, error, etc.).
      const screenshotPath = path.join(__dirname, "..", "auth-debug.png");
      await page.screenshot({ path: screenshotPath, fullPage: true });
      console.error(`[auth] waitForURL timed out. Current URL: ${page.url()}`);
      console.error(`[auth] Screenshot saved to: ${screenshotPath}`);
      throw err;
    }
    await page.waitForLoadState("networkidle");

    await context.storageState({ path: STORAGE_STATE });
  } finally {
    await browser.close();
  }
}

export default globalSetup;
