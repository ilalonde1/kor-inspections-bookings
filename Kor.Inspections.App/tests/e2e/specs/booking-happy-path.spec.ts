import { expect, test } from "@playwright/test";

const TEST_PROJECT_NUMBER = process.env.TEST_PROJECT_NUMBER;
const TEST_CONTACT_EMAIL = process.env.TEST_CONTACT_EMAIL;
const TEST_CONTACT_NAME = process.env.TEST_CONTACT_NAME ?? "E2E Test Contact";
const TEST_CONTACT_PHONE = process.env.TEST_CONTACT_PHONE ?? "(604)-555-0100";
const TEST_CONTACT_ADDRESS = process.env.TEST_CONTACT_ADDRESS ?? "100 E2E Test Street";

test.skip(
    !TEST_PROJECT_NUMBER || !TEST_CONTACT_EMAIL,
    "Requires TEST_PROJECT_NUMBER and TEST_CONTACT_EMAIL env vars. " +
    "TEST_CONTACT_EMAIL's domain must be pre-trusted for the project (no OTP)."
);

test("public booking happy path: search → contact → AM book → confirm → list", async ({ page }) => {
    const commentMarker = `e2e ${Date.now()}`;
    const project = TEST_PROJECT_NUMBER!;
    const email = TEST_CONTACT_EMAIL!;

    // ---------- Step 1: project search ----------
    await page.goto("/");
    await expect(page.getByRole("heading", { name: "Book a Field Review" })).toBeVisible();

    await page.locator("#ProjectNumber").fill(project);
    await page.getByRole("button", { name: "Search" }).click();

    const suggestions = page.locator("#projectSuggestions");
    await expect(suggestions).toBeVisible({ timeout: 15000 });
    await suggestions.locator(".project-suggestion-item").first().click();

    // ---------- Step 2: contact email — domain must be trusted, OTP UI must stay hidden ----------
    await page.locator("#ContactEmail").fill(email);
    await expect(page.locator("#projectEmailVerificationBox")).toBeHidden();

    // ---------- Step 3: assign contact via modal ----------
    await page.getByRole("button", { name: /Assign Contact/i }).click();
    const modal = page.locator("#contactModal");
    await expect(modal).toBeVisible();

    const contactRows = page.locator("#contactLookupArea .contact-row");
    await page.waitForTimeout(500); // allow contact list AJAX to settle
    const existingContactCount = await contactRows.count();

    if (existingContactCount > 0) {
        await contactRows.first().locator('[data-action="select"]').click();
    } else {
        await page.locator("#modalContactName").fill(TEST_CONTACT_NAME);
        await page.locator("#modalContactPhone").fill(TEST_CONTACT_PHONE);
        await page.locator("#modalContactAddress").fill(TEST_CONTACT_ADDRESS);
        await page.locator("#modalContactEmail").fill(email);
        await page.locator("#saveContactBtn").click();
    }

    // After contact pick the modal closes and Step 2 fields populate.
    await expect(page.locator("#step2Fieldset")).toBeEnabled({ timeout: 15000 });

    // ---------- Step 4: pick a weekday date (without firing the onChange postback) ----------
    const targetDate = nextAllowedWeekday();
    await page.locator("#requestedDate").evaluate((el, dateStr) => {
        const fp = (el as unknown as { _flatpickr?: { setDate: (d: string, trigger: boolean) => void } })._flatpickr;
        if (fp) {
            fp.setDate(dateStr, false);
        } else {
            (el as HTMLInputElement).value = dateStr;
        }
    }, targetDate);

    // ---------- Step 5: AM preference + marker comment ----------
    await page.locator("#RequestedTime").selectOption("AM");
    await page.locator("#Comments").fill(commentMarker);

    // ---------- Step 6: submit ----------
    await page.getByRole("button", { name: "Request Field Review" }).click();

    // ---------- Step 7: land on /Confirm with the marker visible ----------
    await expect(page).toHaveURL(/\/Confirm/i, { timeout: 15000 });
    await expect(page.getByText(commentMarker)).toBeVisible();

    // ---------- Step 8: booking surfaces in the project inspections list ----------
    await page.goto(`/Inspections/ByProject?projectNumber=${encodeURIComponent(project)}&email=${encodeURIComponent(email)}`);
    await expect(page.locator("#inspectionsTable")).toBeVisible({ timeout: 15000 });
    await expect(page.locator("#inspectionsBody")).toContainText(commentMarker);
});

function nextAllowedWeekday(): string {
    const now = new Date();
    // Server cuts off at 14:00 PT for next-day bookings. Add two days from
    // anywhere in the day to stay safely inside the booking window.
    const candidate = new Date(now);
    candidate.setDate(candidate.getDate() + 2);
    while (candidate.getDay() === 0 || candidate.getDay() === 6) {
        candidate.setDate(candidate.getDate() + 1);
    }
    const yyyy = candidate.getFullYear();
    const mm = String(candidate.getMonth() + 1).padStart(2, "0");
    const dd = String(candidate.getDate()).padStart(2, "0");
    return `${yyyy}-${mm}-${dd}`;
}
