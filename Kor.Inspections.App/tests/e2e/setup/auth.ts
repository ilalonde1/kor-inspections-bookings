import { chromium, type FullConfig } from "@playwright/test";

async function globalSetup(config: FullConfig) {
  const baseUrl = process.env.BASE_URL || config.projects[0]?.use?.baseURL || "http://localhost:5000";
  const email = process.env.TEST_ADMIN_EMAIL;
  const password = process.env.TEST_ADMIN_PASSWORD;

  if (!email || !password) {
    throw new Error("TEST_ADMIN_EMAIL and TEST_ADMIN_PASSWORD must be set for Playwright auth setup.");
  }

  const browser = await chromium.launch();
  const page = await browser.newPage();

  try {
    await page.goto(String(baseUrl), { waitUntil: "networkidle" });

    await page.fill('input[type="email"]', email);
    await page.click('input[type="submit"]');

    await page.waitForSelector('input[type="password"]', { state: "visible" });
    await page.fill('input[type="password"]', password);
    await page.click('input[type="submit"]');

    const staySignedInButton = page.locator('input[type="submit"]');
    if (await staySignedInButton.isVisible().catch(() => false)) {
      await staySignedInButton.click();
    }

    await page.waitForLoadState("networkidle");
    await page.context().storageState({ path: "storageState.json" });
  } finally {
    await browser.close();
  }
}

export default globalSetup;
