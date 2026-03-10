import { expect, test } from "@playwright/test";

test("admin mobile inspector tools smoke", async ({ page }) => {
  await page.goto("/admin");
  await page.waitForLoadState("domcontentloaded");

  const body = page.locator("body");
  await expect(body).toHaveClass(/mobile-mode/);
  await expect(body).toHaveClass(/inspector-mode/);

  await expect(page.locator("#adminMobileTodayPanel")).toBeVisible();
  await expect(page.locator("#adminMobileInspectorBar")).toBeVisible();

  const bookingRows = page.locator("tr.booking-row");
  const rowCount = await bookingRows.count();

  if (rowCount > 0) {
    const listButtons = page.locator("#mobileTodayList button");
    await expect(listButtons.first()).toBeVisible();
    await listButtons.first().click();

    const callBtn = page.locator(".mobile-call");
    const mapBtn = page.locator(".mobile-map");

    const callHref = await callBtn.getAttribute("href");
    const mapHref = await mapBtn.getAttribute("href");

    if (callHref) {
      expect(callHref.startsWith("tel:")).toBeTruthy();
    } else {
      await expect(callBtn).toHaveAttribute("aria-disabled", "true");
    }

    if (mapHref) {
      expect(mapHref.startsWith("https://www.google.com/maps/search/?api=1&query=")).toBeTruthy();
    } else {
      await expect(mapBtn).toHaveAttribute("aria-disabled", "true");
    }
  }
});
