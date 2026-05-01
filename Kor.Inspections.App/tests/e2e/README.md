# E2E Tests

Playwright specs that drive the running app from a browser.

## What's covered
- **booking-core.spec.ts** — page-load smoke (home + admin) and basic field visibility.
- **admin-mobile-inspector.spec.ts** — `/admin` on iPhone viewport adds `mobile-mode`/`inspector-mode` to `<body>`, inspector panel + action bar render, Call/Map links are wired or properly disabled.
- **booking-happy-path.spec.ts** — full public booking flow: project search → contact pick → AM booking submit → /Confirm → booking surfaces in `/Inspections/ByProject`. Skips automatically if its env vars are unset.

## Required environment variables
Always required:
- `BASE_URL` (e.g. `https://localhost:7074`)
- `TEST_ADMIN_EMAIL`
- `TEST_ADMIN_PASSWORD`

Required for `booking-happy-path.spec.ts` (otherwise the spec skips):
- `TEST_PROJECT_NUMBER` — a project number that Deltek search returns and that has a verified booking history (i.e. at least one trusted contact-email domain on `ProjectDefaults`).
- `TEST_CONTACT_EMAIL` — an email whose domain is pre-trusted for that project, so the OTP step does not appear.
- `TEST_CONTACT_NAME` (optional, default `E2E Test Contact`) — used only when no existing contact exists for the project+domain pair and the spec creates one.
- `TEST_CONTACT_PHONE` (optional, default `(604)-555-0100`).
- `TEST_CONTACT_ADDRESS` (optional, default `100 E2E Test Street`).

The happy-path spec creates a real booking each run and does not clean it up — admins should cancel any leftover `e2e <timestamp>` bookings via the admin grid as needed.

## Run
1. Start the app (example): `dotnet run`
2. In another terminal:
   - `npm --prefix tests/e2e install`
   - `npx playwright install`
   - `set BASE_URL=http://localhost:5000`
   - `set TEST_ADMIN_EMAIL=admin@example.com`
   - `set TEST_ADMIN_PASSWORD=your-password`
   - `npm --prefix tests/e2e test`

The Playwright global setup authenticates once per test run and saves the session
to `storageState.json` for reuse across tests.
