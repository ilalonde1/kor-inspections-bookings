import { expect, test } from "@playwright/test";

test("home page loads", async ({ page }) => {
  await page.goto("/");

  await expect(page).toHaveURL(/localhost/);

  await expect(page.getByRole("heading", { name: "Book a Field Review" }))
    .toBeVisible();
  await expect(page.getByRole("button", { name: "Request Field Review" }))
    .toBeVisible();
});

test("admin page loads", async ({ page }) => {
  await page.goto("/admin");

  await expect(page).toHaveURL(/admin/);

  await expect(page.getByRole("heading", { name: "Field Reviews" })).toBeVisible();
  await expect(page.getByRole("link", { name: "View Tomorrow's Summary" })).toBeVisible();
});

test("booking lookup form visible", async ({ page }) => {
  await page.goto("/");

  const emailInput = page.locator("#ContactEmail");
  const projectInput = page.locator("#ProjectNumber");
  const lookupButton = page.getByRole("button", { name: "Search" });

  await expect(emailInput).toBeVisible();
  await expect(projectInput).toBeVisible();
  await expect(lookupButton).toBeVisible();
});
