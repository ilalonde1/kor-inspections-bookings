# E2E Smoke Test (Mobile Admin Inspector)

This folder contains one Playwright smoke test for the mobile admin inspector UI.

## What it checks
- `/admin` on iPhone viewport adds `mobile-mode` and `inspector-mode` to `<body>`
- inspector mobile panel and action bar are visible
- if booking rows exist, Call/Map links are either correctly wired or disabled

## Required environment variables
- `BASE_URL`
- `TEST_ADMIN_EMAIL`
- `TEST_ADMIN_PASSWORD`

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
