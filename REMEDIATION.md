# KOR Inspections Bookings — Remediation Backlog

Author: Claude (codebase ingestion pass)
Date: 2026-04-21
Scope: Findings from a full-codebase read of `Kor.Inspections.App` + `Kor.Inspections.Tests` + `tests/e2e`.

How to use this file:
1. Each numbered item is sized for **one Codex prompt**.
2. The **Acceptance** block is what I will check before committing — Codex must satisfy every bullet there.
3. Items are ranked by impact + sequencing (do P0 → P3 in order; respect `Depends on` links inside a band).
4. Items not yet done remain checked-off `[ ]`. Mark `[x]` when committed; leave a SHA and date.

---

## P0 — Security / data exposure

### 1. [ ] Rotate every secret committed to `appsettings*.json` and move to env vars
- **Files**: `Kor.Inspections.App/appsettings.json` (lines 3, 8, 14, 18), `Kor.Inspections.App/appsettings.Production.json` (line 3)
- **Problem**: SQL password, Graph client secret, Azure AD client secret, and Deltek ODBC DSN credentials are in tracked config. `TrustServerCertificate=True` is also set on the SQL connection.
- **Fix**:
  1. Replace each secret value in `appsettings.json` and `appsettings.Production.json` with the literal placeholder `__SET_VIA_ENV__` (matches the existing detector in `Program.cs:211`).
  2. Confirm `Program.cs` `ValidateRequiredSecret` already throws on `__SET_` in production — no code change needed there.
  3. Document the required env-var names in a new `docs/secrets.md` (one file, ~30 lines): `ConnectionStrings__Sql`, `Graph__ClientSecret`, `AzureAd__ClientSecret`, `Deltek__OdbcDsn`.
- **Out of Codex's scope (manual)**: actually rotating the secrets in Azure / SQL / Deltek and pushing env vars to the host. Codex only edits the files.
- **Acceptance**:
  - `git grep -nE "(ChangeThisStrongPassword|lHV8Q~AcPYpV69rFAThwK9uuqYqcARD_aJmSIbpw|dV88Q~JA6hvOsjFGi0ixJrjSUQibQR0qimSv5dly|2Mn8Q~9j0XkVuCWCFC_InJd5hz0fCnOXumN_BaAJ|SSgdmOkSR6p9Gf)"` returns nothing.
  - `dotnet build Kor.Inspections.App` succeeds.
  - Setting the four env vars and running with `ASPNETCORE_ENVIRONMENT=Production` does not throw at startup; unsetting any one of them throws `InvalidOperationException` with the missing key in the message.
- **Risk**: zero code-path change; deployment must be updated in lockstep or the prod app will refuse to start (which is the intended safety behavior).
- **Note**: history scrubbing (`git filter-repo` or BFG) is a separate manual step; do not delegate to Codex.

### 2. [ ] Gate `OnPostLookupInspectionsAsync` on a verified server-side identity
- **Files**: `Kor.Inspections.App/Pages/Index.cshtml.cs:334-386`, `Kor.Inspections.App/Pages/Inspections/ByProject.cshtml`, `Kor.Inspections.App/Pages/Inspections/ByProject.cshtml.cs`
- **Problem**: `LookupInspections` only checks `EnsureVerifiedForProjectAccessAsync(project, email)`. The "verified" state is keyed by `proj-bootstrap:{project}|{email}` in memory or by a `ProjectDefaults` row keyed by domain. A caller who knows a project number and any email at a trusted domain can request another contact's inspections; a caller who has not verified at all can hand-craft the POST and get a 403, but `ByProject.cshtml` happily accepts arbitrary `email` query values, hiding that the page itself is not authenticated.
- **Fix**:
  1. In `OnPostLookupInspectionsAsync`, after the existing verification check, additionally require that the `proj-bootstrap` cache entry was minted for **this specific email** (not just any email at the same domain). The current `GetStatusAsync` already supports per-user cache hits — make `EnsureVerifiedForProjectAccessAsync` return `true` only when the per-email cache entry exists, not when only the domain trust exists. (Domain trust is item #4.)
  2. In `ByProject.cshtml.cs`, add a check in `OnGetAsync`: if `!HasRequiredQuery` or if `EnsureVerifiedForProjectAccessAsync(ProjectNumber, Email)` returns false, set a flag and render a "Verify your email to view inspections" message instead of the inspections table. Do not call the JS.
  3. Add an integration test under `Kor.Inspections.Tests/Pages/LookupInspectionsAuthorizationTests.cs` proving that a domain-only trust (ProjectDefaults row exists, no per-email cache entry) does **not** allow lookup.
- **Acceptance**:
  - The new test is added and passes.
  - All existing tests in `LookupInspectionsAuthorizationTests` still pass.
  - Manual: visit `/Inspections/ByProject?projectNumber=30844&email=stranger@trusted.com` without verifying → page shows the verification gate, not inspection rows.
- **Risk**: existing customers relying on domain-wide trust will need to re-verify per email. Acceptable; expected.
- **Depends on**: #4 (domain-trust semantics). Land #4 first.

### 3. [ ] Decide and document what populates `ProjectDefaults`
- **Files**: `Kor.Inspections.App/Services/ProjectBootstrapVerificationService.cs`, `Kor.Inspections.App/Services/ProjectProfileService.cs:94-119`, `Kor.Inspections.App/Pages/Admin/TrustedDomains.cshtml.cs`
- **Problem**: `ProjectBootstrapVerificationService.VerifyCodeAsync` only mutates the in-memory cache; it never inserts a `ProjectDefaults` row. `HasExplicitDomainApprovalAsync` reads `ProjectDefaults` as if it were a 30-day persistent trust. The only writer to `ProjectDefaults` in the codebase is `ProjectProfileService.SaveDefaultAddressAsync` — which has no callers. Net effect: **persistent domain trust has no documented populating path**, yet the admin page treats `ProjectDefaults` rows as "trusted domains".
- **Fix**: This is a research+decision task, not a typing task. Codex should produce a 1-page `docs/project-trust.md` that:
  1. Greps the codebase and confirms no caller inserts into `ProjectDefaults`.
  2. Greps the database (DBA action — Codex documents the SQL: `SELECT TOP 50 * FROM ProjectDefaults ORDER BY UpdatedUtc DESC`).
  3. Lists the three plausible options:
     - **(A)** Persist on successful verification: have `VerifyCodeAsync` upsert a `ProjectDefaults` row.
     - **(B)** Drop persistent trust entirely: remove `HasExplicitDomainApprovalAsync` and the `TrustedDomains` admin page, treat verification as in-memory-only (8h TTL).
     - **(C)** Status quo + admin-only seeding: trust rows can only be created by a future "Approve domain" admin button.
  4. Recommends one, with rationale tied to remediation #2.
- **Acceptance**:
  - `docs/project-trust.md` exists and contains the three options with a recommendation.
  - No code change is made by this ticket — only docs + decision.
- **Risk**: none (docs only).
- **Depends on**: nothing. **Blocks**: #2 and #4.

---

## P1 — Authorization correctness

### 4. [ ] Tighten domain-trust → user-trust in `ProjectBootstrapVerificationService`
- **Files**: `Kor.Inspections.App/Services/ProjectBootstrapVerificationService.cs:194-230`
- **Problem**: `HasExplicitDomainApprovalAsync` returns `true` for **any** email at the trusted domain, not the verified email. Combined with #2 this is the actual privilege widening.
- **Fix** (assuming #3 picks option A or B):
  - **If A**: keep `HasExplicitDomainApprovalAsync` but add a `ProjectTrustedUsers` table (project + email + expiresUtc) that takes precedence over the domain check. Update `VerifyCodeAsync` to insert/refresh the row. Migration required.
  - **If B**: delete `HasExplicitDomainApprovalAsync`, the `ProjectDefaults` writes from this service, and the `TrustedDomains` page entirely. Verification becomes pure in-memory.
- **Acceptance**:
  - Existing `TrustedDomainsModelTests` pass (or are deleted, depending on path).
  - New tests cover: (i) verifying user A does not authorize user B at the same domain; (ii) revoke (admin) immediately denies the user.
- **Risk**: medium. Existing trusted users may need to re-verify.
- **Depends on**: #3.

### 5. [ ] Make cancellation copy and the cutoff hour share one source of truth
- **Files**: `Kor.Inspections.App/Services/BookingService.cs:591-594`, `Kor.Inspections.App/Pages/Index.cshtml:21`, `Kor.Inspections.App/Pages/Manage.cshtml:61`, `Kor.Inspections.App/Options/InspectionRulesOptions.cs`
- **Problem**: "2:00 p.m. Pacific" and "Monday to Friday, 7:30 a.m. to 4:00 p.m." are hardcoded in three places. Changing `InspectionRules:CutoffHourLocal` or `WorkStart`/`WorkEnd` in config silently desyncs user-facing copy.
- **Fix**: Add a small helper (e.g., `InspectionRulesCopy.GetCutoffSentence(InspectionRulesOptions)`) that formats `CutoffHourLocal` and the work window. Inject `IOptions<InspectionRulesOptions>` into `ConfirmModel`, `ManageModel`, and use it in the two Razor pages and in `BookingService.BuildDetailedBookingHtml`.
- **Acceptance**:
  - Setting `CutoffHourLocal: 15` in `appsettings.Development.json` and running the app produces "3:00 p.m." in the booking confirmation HTML, the `/` page intro, and the `/Manage` blocked-cancellation message.
  - No string `"2:00 p.m."` remains in `*.cs` or `*.cshtml` files.
- **Risk**: none beyond the touched copy.

### 6. [ ] Stop deleting `ProjectDefault` rows on revoke; mark them revoked instead
- **Files**: `Kor.Inspections.App/Pages/Admin/TrustedDomains.cshtml.cs:52-66`, `Kor.Inspections.App/Data/Models/ProjectDefault.cs`, new migration
- **Problem**: `OnPostRevokeAsync` calls `Remove(row)`. If we ever begin storing `DefaultAddress` on these rows again (the column exists but is unused), revoke would silently delete the address.
- **Fix**:
  1. Add `RevokedUtc DateTime? null` to `ProjectDefault.cs` and a migration.
  2. Update `HasExplicitDomainApprovalAsync` to treat `RevokedUtc != null` as "not approved".
  3. Change `OnPostRevokeAsync` to set `RevokedUtc = DateTime.UtcNow` and `SaveChangesAsync()` instead of `Remove`.
  4. Update `TrustedDomainRow` to surface revoked state in the admin grid.
- **Acceptance**:
  - New migration runs cleanly forward and back on LocalDB.
  - Existing `TrustedDomainsModelTests.OnPostRevokeAsync_ValidId_*` is updated to assert the row still exists with `RevokedUtc != null`.
  - New test: revoke → `GetStatusAsync` returns `IsVerified == false`.
- **Risk**: small data-model change; coordinated with #3/#4 if option B is chosen (in which case skip this entirely).
- **Depends on**: #3 (only relevant if option A or C).

---

## P2 — Hygiene that prevents future bugs

### 7. [ ] Collapse the duplicate `TimePreference` migrations
- **Files**: `Kor.Inspections.App/Migrations/20260212012729_Fix_TimePreferenceNullable.cs`, `Kor.Inspections.App/Migrations/20260212123000_MakeTimePreferenceNullable.cs`
- **Problem**: Both migrations make `TimePreference` nullable; the second is a no-op against the schema after the first, but both shipped.
- **Fix**: Confirm via `__EFMigrationsHistory` query (manual SQL) that production has applied **both** rows. If yes, leave them — removing applied migrations is risky. If only one was applied, delete the redundant one and re-snapshot. Codex output: a short note in the PR description with the recommended SQL to run, and **only delete the file if the user confirms in the PR comment**.
- **Acceptance**:
  - PR description includes the verification SQL and the explicit "do not merge until production state is confirmed" gate.
- **Risk**: high if mis-applied; that's why this is a confirm-then-act ticket.

### 8. [ ] Delete the two empty migrations or document why they exist
- **Files**: `Kor.Inspections.App/Migrations/20260210022234_AddUniqueIndex_ProjectContacts_Email.cs`, `Kor.Inspections.App/Migrations/20260211081113_SyncTimePreference.cs`
- **Problem**: Both have empty `Up` and `Down`. Likely added to sync the snapshot after a manual edit.
- **Fix**: Add a one-line `// <inheritdoc />`-adjacent comment in each `Up` method explaining "intentionally empty: snapshot-sync only after <reason>". Do **not** delete (deleting applied migrations breaks `__EFMigrationsHistory` ordering).
- **Acceptance**:
  - Both files compile and contain a clear comment.
  - `dotnet ef migrations list` is unchanged.
- **Risk**: none.

### 9. [ ] Standardize admin email error handling in `SummaryModel`
- **Files**: `Kor.Inspections.App/Pages/Admin/Summary.cshtml.cs:228-297` (the `OnPostEmailAllInspectorsAsync` path) vs the `TrySendSummaryEmailAsync` helper used elsewhere.
- **Problem**: `OnPostEmailAllInspectorsAsync` does its own try/catch instead of routing through `TrySendSummaryEmailAsync`, so logged messages are formatted differently and the StatusMessage logic is duplicated.
- **Fix**: Extract a `TrySendInspectorBatchAsync(IEnumerable<(string email, string subject, string html)>)` helper that returns `(sent: List<string>, failed: List<string>)` and call it from `OnPostEmailAllInspectorsAsync`. Reuse the same logging template as `TrySendSummaryEmailAsync`.
- **Acceptance**:
  - All `SummaryModelEmailTests` still pass.
  - The `OnPostEmailAllInspectorsAsync` method is < 30 lines.
- **Risk**: low; bounded by existing test coverage.

### 10. [ ] Stop mutating the `IOptions<InspectionRulesOptions>` singleton in `TimeRuleService`
- **Files**: `Kor.Inspections.App/Services/TimeRuleService.cs:19-24`
- **Problem**: `_options.MaxBookingsPerSlot = Math.Max(1, _options.MaxBookingsPerSlot)` writes back into the shared options object. Idempotent today, but a foot-gun.
- **Fix**: Store a private `_maxBookingsPerSlot` int on the service, initialized in the ctor; never mutate `_options`.
- **Acceptance**:
  - `_options` is read-only-by-convention everywhere in the file.
  - `TimeRuleServiceTests` still pass.
- **Risk**: none.

### 11. [ ] Restrict `/healthz` to a non-OIDC scheme suitable for monitors
- **Files**: `Kor.Inspections.App/Program.cs:33-37, 202-204`
- **Problem**: `HealthzAccess` requires an authenticated user, but the only auth scheme is interactive OIDC — so a monitor cannot use `/healthz` without a human in the loop. Also `AddDbContextCheck` opens a DB connection per probe.
- **Fix**:
  1. Add a static `Health:ProbeKey` config (env var in prod). Add an `ApiKeyAuthenticationHandler` (or similar minimal scheme) registered alongside OIDC.
  2. `HealthzAccess` policy accepts either an authenticated OIDC user **or** a request bearing `X-Health-Key: {ProbeKey}`.
  3. Replace `AddDbContextCheck` with a lightweight `AddSqlServer(connectionString, healthQuery: "SELECT 1")` so probes don't open EF Core connections.
- **Acceptance**:
  - `HealthzEndpointTests` updated: still 401 without header, 200 with valid key.
  - In dev (no `Health:ProbeKey` set), the endpoint returns 401 to anonymous calls (no behavior regression).
- **Risk**: medium; touches startup auth wiring. Land after CI is green on smaller items.

### 12. [ ] Remove unused `UseSession()` and the related options
- **Files**: `Kor.Inspections.App/Program.cs:38-46, 187-188`
- **Problem**: Distributed memory cache + Session middleware are wired but no code reads/writes `HttpContext.Session`. Confirmed by `git grep -n HttpContext.Session` returning nothing.
- **Fix**: Delete `AddDistributedMemoryCache`, `AddSession`, `UseSession()`. Keep `AddMemoryCache` (it is used by Deltek + verification).
- **Acceptance**:
  - `git grep -n "AddSession\|UseSession\|HttpContext.Session"` returns nothing.
  - All tests pass.
- **Risk**: none.

### 13. [ ] Delete the dead JS in `Admin/Summary.cshtml`
- **Files**: `Kor.Inspections.App/Pages/Admin/Summary.cshtml:332-358`
- **Problem**: A `DOMContentLoaded` listener wires `[data-inline-confirm-form]` / `[data-confirm-trigger]` / `[data-confirm-controls]` / `[data-confirm-cancel]` but no markup uses these attributes (verified by grep).
- **Fix**: Delete the entire second `<script>` block (lines 332-358).
- **Acceptance**:
  - `git grep -n "data-inline-confirm-form\|data-confirm-trigger"` returns nothing.
  - Playwright admin smoke test still passes.
- **Risk**: none.

### 14. [ ] Either DI-register or delete `ProjectAccessService` (and its tests)
- **Files**: `Kor.Inspections.App/Services/ProjectAccessService.cs`, `Kor.Inspections.Tests/Services/ProjectAccessServiceTests.cs`
- **Problem**: PIN-based auth was superseded by the email OTP flow. The service is only referenced by its own tests; never registered in DI.
- **Fix**: Decide (state in PR description): if dead, delete both files. If kept "in case", add `[Obsolete("...")]` on the class with a clear migration note and add an explicit DI registration (commented out is fine) so a future maintainer doesn't think DI is missing.
- **Acceptance**:
  - Either both files are gone OR both have a clear obsolete marker plus an explanatory comment in `Program.cs` near the other service registrations.
- **Risk**: none.

### 15. [ ] Delete the unused vendored client libraries
- **Files**: `Kor.Inspections.App/wwwroot/lib/{bootstrap,jquery,jquery-validation,jquery-validation-unobtrusive}/`
- **Problem**: `_Layout.cshtml` does not reference any of these. They ship to no one.
- **Fix**: Delete the four directories. Remove any LibMan / package.json that references them (none found, but double-check).
- **Acceptance**:
  - `git grep -n "wwwroot/lib"` returns nothing.
  - Playwright tests still pass.
- **Risk**: low; if a future Razor scaffolded view assumes jquery validation, it will break — flag this in the PR description.

### 16. [ ] Pin the Flatpickr CDN reference
- **Files**: `Kor.Inspections.App/Pages/Index.cshtml:5, 1318-1320`
- **Problem**: `<link rel="stylesheet" href="https://cdn.jsdelivr.net/npm/flatpickr/dist/flatpickr.min.css">` and `<script src="https://cdn.jsdelivr.net/npm/flatpickr">` use unpinned URLs and no SRI hash.
- **Fix**: Pin to a specific version (e.g., `flatpickr@4.6.13`) in the URLs and add `integrity="sha384-..."` + `crossorigin="anonymous"` for both. Use the official jsDelivr SRI generator.
- **Acceptance**:
  - Both URLs include `@<version>` and an `integrity` attribute.
  - The booking page still loads in Playwright and the date picker still opens.
- **Risk**: very low; pin only.

### 17. [ ] Fix `ProjectCacheKeys.BuildVerificationKey` parameter naming
- **Files**: `Kor.Inspections.App/Services/ProjectCacheKeys.cs`
- **Problem**: Second parameter is named `domain` but every caller passes a full email; the function lowercases it (works for both) but the name lies.
- **Fix**: Rename parameter to `email` and add a one-line summary doc.
- **Acceptance**:
  - The signature is `BuildVerificationKey(string projectNumber, string email)`.
  - All existing tests pass.
- **Risk**: none.

### 18. [ ] Replace fragile `_Layout.cshtml` admin detection
- **Files**: `Kor.Inspections.App/Pages/Shared/_Layout.cshtml:33-38`
- **Problem**: Detects "is admin page" via `ViewContext.RouteData.Values["page"]?.ToString()?.StartsWith("/Admin", …)`. Razor Pages routing changes (or a future area rename) silently break the layout switch.
- **Fix**: Use `ViewContext.HttpContext.Request.Path.StartsWithSegments("/admin", StringComparison.OrdinalIgnoreCase)`. Both `/Admin/Index` and the explicit `/admin` page route map to a path beginning with `/admin`.
- **Acceptance**:
  - Loading `/Admin/Index`, `/Admin/Summary`, `/admin/trusted-domains`, and `/` produces the right container class (`admin-page-container` for the first three, `content page-container` for the fourth).
- **Risk**: low; sanity-check via Playwright admin smoke.

### 19. [ ] Move `BookingDisplayHelper`'s "Anytime AM/PM" labels behind config
- **Files**: `Kor.Inspections.App/Services/BookingDisplayHelper.cs`, `Kor.Inspections.App/Options/InspectionRulesOptions.cs`
- **Problem**: Strings "Anytime AM" / "Anytime PM" are hardcoded in three places (helper, BookingService email, Index page select). Same kind of drift risk as #5.
- **Fix**: Add `AmLabel = "Anytime AM"` and `PmLabel = "Anytime PM"` to `InspectionRulesOptions`. Read via `IOptions<InspectionRulesOptions>` everywhere it's used. Defaults preserve current copy.
- **Acceptance**:
  - Helper signature accepts the labels (or it's converted to an instance class with options injected).
  - All existing tests pass; UI strings unchanged with default config.
- **Risk**: low.

---

## P3 — Cleanup / housekeeping

### 20. [ ] Delete `Kor.Inspections.Tests/UnitTest1.cs`
- **Files**: that file
- **Problem**: scaffolded empty test.
- **Fix**: delete.
- **Acceptance**: file gone, `dotnet test` passes.
- **Risk**: none.

### 21. [ ] Delete `tests/e2e/auth-debug.png`
- **Files**: `Kor.Inspections.App/tests/e2e/auth-debug.png`
- **Problem**: 42 KB committed debug artifact.
- **Fix**: delete the file. Confirm it is generated only on auth failure (already verified in `setup/auth.ts:53`) — leave the generation code in place.
- **Acceptance**: file gone; `setup/auth.ts` unchanged.
- **Risk**: none.

### 22. [ ] Decide the fate of root-level `tables.csv` and `columns.csv`
- **Files**: `tables.csv` (75 KB), `columns.csv` (1.5 MB) at repo root
- **Problem**: not consumed by code; purpose unclear from the codebase.
- **Fix**: Codex should grep for any `tables.csv` / `columns.csv` reference in the repo and report. If none, move both to `docs/deltek-schema/` with a one-line README explaining provenance, or delete. **User decides** in the PR comment which.
- **Acceptance**: PR description states the grep result and proposes one of {move, delete, keep}.
- **Risk**: none.

### 23. [ ] Bring the `dotnet-ef` tool version in line with EF Core packages
- **Files**: `Kor.Inspections.App/.config/dotnet-tools.json`
- **Problem**: tool pinned to 10.0.2 while EF Core packages are 8.0.11.
- **Fix**: change the version to `8.0.11` (or the latest 8.0.x). Run `dotnet tool restore` locally to confirm.
- **Acceptance**:
  - `dotnet tool run dotnet-ef --version` reports 8.0.x.
  - `dotnet ef migrations list --project Kor.Inspections.App/Kor.Inspections.App.csproj` succeeds.
- **Risk**: none.

### 24. [ ] Tighten `FolderProfile.pubxml` to avoid stale files
- **Files**: `Kor.Inspections.App/Properties/PublishProfiles/FolderProfile.pubxml:5-7`
- **Problem**: `<DeleteExistingFiles>false</DeleteExistingFiles>` means removed files survive deploys.
- **Fix**: Set `<DeleteExistingFiles>true</DeleteExistingFiles>`. Add a comment warning the developer that `_publish_audit/` etc. should not point at a directory holding unrelated files.
- **Acceptance**: setting is `true`; PR description notes the change so the next manual publish doesn't surprise anyone.
- **Risk**: medium operational. Land after announcing on whatever channel the team uses.

### 25. [ ] Add a desktop Playwright project alongside `mobile-safari`
- **Files**: `Kor.Inspections.App/tests/e2e/playwright.config.ts`
- **Problem**: only iPhone 13 viewport is tested; admin UX (drag-drop route planner, etc.) only works at desktop sizes and has no e2e coverage.
- **Fix**: add a `desktop-chromium` project using `devices["Desktop Chrome"]`, sharing `storageState`. Move `admin-mobile-inspector.spec.ts` to run only under `mobile-safari` (set `project: 'mobile-safari'` in test annotation or use a `testMatch` filter).
- **Acceptance**:
  - `npx playwright test` runs four tests across two projects with the existing tests still passing.
- **Risk**: low.

### 26. [ ] Stop committing `Properties/PublishProfiles/FolderProfile.pubxml` history
- **Files**: `Kor.Inspections.App/.gitignore`, `Kor.Inspections.App/Properties/PublishProfiles/FolderProfile.pubxml`
- **Problem**: the `.pubxml` is tracked, but its `<History>` block in the `.user` sidecar (already gitignored) is the only thing that changes. The current branch shows `M ...FolderProfile.pubxml` as a noisy diff.
- **Fix**: Look at the actual diff in `git diff Kor.Inspections.App/Properties/PublishProfiles/FolderProfile.pubxml`. If only the `History` line changed (it shouldn't be in the non-`.user` file), revert. Otherwise, commit deliberately. Codex output: report the diff, recommend.
- **Acceptance**: `git status` shows the file as clean, or the modification is intentional and explained.
- **Risk**: none.

### 27. [ ] Extract `Pages/Index.cshtml`'s inline JS into a module
- **Files**: `Kor.Inspections.App/Pages/Index.cshtml:308-1316` → move to `Kor.Inspections.App/wwwroot/js/booking-page.js`
- **Problem**: ~1000 lines of JS inline in the Razor view. Hard to lint, hard to cache, and inflates each page render.
- **Fix**: Move the IIFE script block to a new `wwwroot/js/booking-page.js`. Reference via `<script src="~/js/booking-page.js" asp-append-version="true"></script>` in a `@section Scripts`. Pass any server-side values (none are currently injected from Razor — the script reads only from the DOM) via `data-*` attributes if needed.
- **Acceptance**:
  - `Pages/Index.cshtml` no longer contains a multi-hundred-line `<script>` block.
  - Playwright booking-core specs still pass.
  - Manual: end-to-end booking flow still works against the dev server.
- **Risk**: medium — the script is large and untyped. Consider deferring until the rest of the list is clear so it can be reviewed in isolation.

### 28. [ ] Remove the `// CODEX TEST  verified update` stray comment
- **Files**: `Kor.Inspections.App/Services/BookingService.cs:1`
- **Problem**: leftover marker from a tooling experiment.
- **Fix**: delete the comment.
- **Acceptance**: file starts with `using System;`.
- **Risk**: none.

### 29. [ ] Remove `// ✅ ADD THIS` and `// ADD THIS BLOCK` editor markers
- **Files**: `Kor.Inspections.App/Services/BookingService.cs:463, 510`
- **Problem**: vestigial code-review markers.
- **Fix**: delete the comment lines (the code below them stays).
- **Acceptance**: `git grep -nE "// (✅ )?ADD THIS"` returns nothing.
- **Risk**: none.

---

## Items intentionally NOT on this list

- **Splitting the `IndexModel` 917-line page model**: bigger architecture decision, not a single-prompt task. Revisit after #2/#4 land.
- **Removing `Task.Run` from `DeltekProjectService`**: the bounded semaphore + 60s cache make this acceptable; an actual async ODBC provider would be a bigger swap.
- **Replacing inline JS in `_Layout.cshtml` with an external module**: low value; the inline block is short and bounded.
- **Adding `Booking` indexes beyond what migration `20260314015704_AddBookingReadIndexes` already added**: revisit only with measured query plans.

---

## Suggested execution order

Land in this order to minimize churn:

1. #20, #21, #28, #29, #17 (trivial cleanups; warm up the PR machinery).
2. #1 (secrets — needs ops coordination but Codex part is trivial).
3. #3 (decision document; unblocks #2/#4/#6).
4. #2, #4, #6 (security, in that order).
5. #11, #12, #13 (defensive cleanup).
6. #5, #19 (config-driven copy).
7. #7, #8, #9, #10, #14, #15, #16, #18, #23, #25, #26 (smaller hygiene).
8. #24 (publish profile — needs an ops heads-up).
9. #22 (CSV decision).
10. #27 (Index.cshtml JS extraction — last; biggest blast radius).
