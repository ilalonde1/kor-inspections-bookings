# KOR Inspections Bookings — Remediation Backlog

Source: full audit pass 2026-04-24 (see `AUDIT-2026-04-24.md`).
Against `main` at commit `ac0e056`.

**Workflow**: each item becomes one Codex prompt. User feeds prompt → Codex edits → Claude verifies + commits + pushes. Items may reshuffle as earlier ones land and expose new context.

Severity: 🔴 P0 active bug · 🟠 P1 correctness · 🟡 P2 hygiene · 🟢 P3 operational / nice-to-have.

---

## 🔴 P0 — Real bugs shipping now

### 1. [ ] Admin manual-create rejects AM/PM bookings
- **Files**: `Kor.Inspections.App/Pages/Admin/Index.cshtml.cs:146-149`
- **Problem**: `ManualBookingInput.RequestedTime` has `[RegularExpression(@"^\d{2}:\d{2}$")]`. The form offers `<option value="AM">` / `<option value="PM">`. When admin picks AM or PM, validator rejects with `"Requested time must use HH:mm."` before the handler's AM/PM branch can run.
- **Fix**: change the regex to `^(AM|PM|\d{2}:\d{2})$` (same pattern `EditBookingInput` uses at line 502).
- **Acceptance**: (a) `dotnet build` clean. (b) manually submit admin manual-create with "AM" → succeeds, booking lands with `TimePreference="AM"`, `StartUtc` at 8:00 local. (c) submit with "PM" → same with 12:00 local.
- **Risk**: none. Unblocks admin workflow.

---

## 🟠 P1 — Correctness holes

### 2. [ ] Weekend dates accepted for creation; cancellation rules assume weekdays
- **Files**: `Kor.Inspections.App/Services/TimeRuleService.cs:33-51` (`GetAllowedDateRangeUtcNow`), reference logic at line 137-141 (`IsCancellationAllowed`)
- **Problem**: `GetAllowedDateRangeUtcNow` computes `minDate = today+1 or today+2` with no Sat/Sun skip. Friday post-cutoff → `minDate = Sunday`. Users can submit Sunday bookings. Meanwhile `IsCancellationAllowed` DOES skip weekends. Two halves disagree.
- **Fix**: in `GetAllowedDateRangeUtcNow`, after computing the candidate `minDate`, advance past Saturday/Sunday the same way `IsCancellationAllowed` does (loop `AddDays(1)` while DayOfWeek is Sat/Sun). Leave `maxDate` alone (caller already clamps slot search at workStart..workEnd per day, so weekend `maxDate` is a no-op).
- **Acceptance**: new `TimeRuleServiceTests` fact: when local "today" is Friday and past cutoff, `GetAllowedDateRangeUtcNow().MinDate.DayOfWeek` is Monday (not Sunday). Existing tests still pass.
- **Risk**: low. If weekend bookings WERE legitimate operations, this blocks them. Confirm with user.

### 3. [ ] Email normalization inconsistent across 6 public handlers (Codex #5)
- **Files**:
  - `Kor.Inspections.App/Pages/Index.cshtml.cs:333, 387, 497, 519, 546, 577-579`
  - `Kor.Inspections.App/Pages/Admin/Index.cshtml.cs:234`
- **Problem**: some handlers lowercase (`.Trim().ToLowerInvariant()`), some only `.Trim()`. Downstream SQL comparisons are exact-match. Default SQL Server collation is CI so it works today; any collation change breaks user lookups.
- **Fix**: add `.ToLowerInvariant()` after every `.Trim()` on email inputs in the listed lines. Admin `ManualBooking.ContactEmail` should also be lowercased before persistence. `PersistRouteOrderAsync` (`Summary.cshtml.cs:515`) gets the same treatment when matching inspector email.
- **Acceptance**: `rg -n '\(req\.Email\s*\?\?\s*""\)\s*\.Trim\(\)(?!\.ToLowerInvariant)' Kor.Inspections.App/Pages/` returns nothing. `dotnet test` passes.
- **Risk**: if any legitimate mixed-case stored email exists in prod, it becomes unreachable without a one-time backfill. PR description must ask user to check DB with `SELECT DISTINCT ContactEmail FROM Bookings WHERE ContactEmail <> LOWER(ContactEmail)` first.

### 4. [ ] Admin summary email recipient inconsistent with booking-notification recipient
- **Files**: `Kor.Inspections.App/Pages/Admin/Summary.cshtml.cs:177-178`
- **Problem**: `OnPostEmailAsync` sends the summary to `_notificationOptions.FromMailbox`. `BookingService.SendInitialEmailsAsync` sends the admin NEW-BOOKING email to `_notificationOptions.Email`. Two different fields for "the admin recipient".
- **Fix**: change line 178 from `toEmail = _notificationOptions.FromMailbox` → `toEmail = _notificationOptions.Email`.
- **Acceptance**: summary email arrives at `NotificationOptions:Email` in dev. Tests pass.
- **Risk**: none — values equal each other in current config.

### 5. [ ] Summary email missing project-name + hyphenated phone (regressed vs V1.1 #3a/#3b)
- **Files**: `Kor.Inspections.App/Pages/Admin/Summary.cshtml.cs:61-80` (`SummaryRow`), `144-167` (projection), `432-443` (`BuildEmailHtml`)
- **Problem**: Summary email (morning roster) shows just `ProjectNumber` (Base5) and raw `ContactPhone` digits. Every other email in the app uses `FormatJobLine` + `PhoneNormalizer.Format`. Summary regressed.
- **Fix**:
  1. Add `string? ProjectNumberDisplay` and `string? ProjectName` fields to `SummaryRow`.
  2. Populate them in the projection from `b.ProjectNumberDisplay` / `b.ProjectName`.
  3. In `BuildEmailHtml`, the Job column renders `{ProjectNumberDisplay ?? ProjectNumber} {ProjectName}` (trim trailing space when name is null). Helper: reuse a `FormatJobLine` equivalent — duplicate the private helper from `BookingService` or copy the 4-line logic inline.
  4. Contact column formats phone via `PhoneNormalizer.Format(b.ContactPhone)`.
- **Acceptance**: `SummaryModelEmailTests` pass. Manual trigger against dev shows `30961-01 River District Parcel 29` + `604-555-1234`.
- **Risk**: none. Display-only.

### 6. [ ] Reassign-inspector client email says "Scheduled" (confusing for reassignments)
- **Files**: `Kor.Inspections.App/Services/BookingService.cs:265-331` (`SendAssignmentEmailAsync`), `Kor.Inspections.App/Pages/Admin/Index.cshtml.cs:430-438`
- **Problem**: After Codex #3 fix, reassignments (A → B) call `SendAssignmentEmailAsync`. Client subject is `"Your Field Review Has Been Scheduled – ..."` → near-duplicate of the original booking confirmation.
- **Fix**: add `bool isReassignment = false` parameter to `SendAssignmentEmailAsync`. When true:
  - Client subject: `"Your Field Review Inspector Has Changed – {FormatJobLine(booking)} – {startLocal:yyyy-MM-dd HH:mm}"`
  - Client body lead: `"The inspector assigned to your field review has been updated. All other details remain the same."`
  - Inspector side unchanged (new inspector "You have been assigned" is correct).
  Update the single caller in `OnPostAssignAsync`: pass `isReassignment: !string.IsNullOrWhiteSpace(oldAssignedTo)`.
- **Acceptance**: update one existing test (`AdminIndexModelReassignmentTests.OnPostAssignAsync_ReassignFromInspectorAToInspectorB_SendsAssignmentEmail`) to verify the reassignment subject string. Add a new test for the initial-assignment subject retained.
- **Risk**: low. Internal API change; one caller.

### 7. [ ] Admin client-assignment email subject missing project identifier
- **Files**: `Kor.Inspections.App/Services/BookingService.cs:280`
- **Problem**: `var clientSubject = $"Your Field Review Has Been Scheduled – {startLocal:yyyy-MM-dd HH:mm}";` — only date in subject. Every other email subject includes `FormatJobLine(booking)`.
- **Fix**: change to `$"Your Field Review Has Been Scheduled – {FormatJobLine(booking)} – {startLocal:yyyy-MM-dd HH:mm}"`.
- **Acceptance**: existing tests still pass. Dev trigger shows project in subject.
- **Risk**: none. (May want to bundle with #6 since both touch `SendAssignmentEmailAsync`.)

### 8. [ ] `BookingSlotUnavailableException` specific message never reaches user
- **Files**: `Kor.Inspections.App/Services/BookingService.cs:162-171, 200-205` (throws), `Kor.Inspections.App/Pages/Index.cshtml.cs:862-868`, `Kor.Inspections.App/Pages/Admin/Index.cshtml.cs:308-316` (catches)
- **Problem**: two distinct throw sites, one says "no longer available" (overlap cap), the other says "This booking was already submitted. Please check your confirmation email." (duplicate-slot unique index). Catch sites always display a hardcoded fallback string. The "already submitted" message never reaches the user.
- **Fix**: in both catch blocks, replace the hardcoded fallback with `ex.Message` (the service-provided text).
- **Acceptance**: manually trigger a duplicate submission → user sees "already submitted" message. Existing tests pass.
- **Risk**: none.

---

## 🟡 P2 — Hygiene / maintenance

### 9. [ ] `CancelToken` on `Booking` has no DB index
- **Files**: `Kor.Inspections.App/Data/InspectionsContext.cs:82-88` + new migration
- **Problem**: Every `/Manage?token=...` hit scans Bookings table. Trivial today, bad at scale.
- **Fix**: add `entity.HasIndex(b => b.CancelToken).IsUnique().HasDatabaseName("IX_Bookings_CancelToken_Unique");`. Generate migration `AddBookingCancelTokenIndex`.
- **Acceptance**: migration is additive (one CREATE INDEX). Tests pass.
- **Risk**: must apply migration to prod with next publish. Standard deploy pattern.

### 10. [ ] `TimeRuleService` mutates `IOptions<InspectionRulesOptions>` singleton
- **Files**: `Kor.Inspections.App/Services/TimeRuleService.cs:19-24`
- **Problem**: ctor mutates `_options.MaxBookingsPerSlot` on the shared singleton. Latent race foot-gun.
- **Fix**: store clamped value in `private readonly int _maxBookingsPerSlot`. Don't mutate `_options`. Replace internal references.
- **Acceptance**: `rg '_options\.MaxBookingsPerSlot\s*=' Kor.Inspections.App/Services/TimeRuleService.cs` returns nothing. Tests pass.
- **Risk**: none.

### 11. [ ] Unused Session middleware
- **Files**: `Kor.Inspections.App/Program.cs:38-46, 188`
- **Problem**: `AddDistributedMemoryCache`, `AddSession`, `UseSession()` wired; no code uses sessions.
- **Fix**: delete those three registrations + `UseSession()`. Keep `AddMemoryCache` (Deltek uses it).
- **Acceptance**: `rg 'HttpContext\.Session|AddSession|UseSession|AddDistributedMemoryCache' Kor.Inspections.App` returns zero matches. App starts, Playwright smoke passes.
- **Risk**: none.

### 12. [ ] Verification OTP code exposed in email subject
- **Files**: `Kor.Inspections.App/Services/ProjectBootstrapVerificationService.cs:142`
- **Problem**: subject is `"KOR verification code: 123456"`. Mobile notification bars / lock screens show the code without unlocking. Security best practice is not to put secrets in subjects.
- **Fix**: change subject to `"KOR verification code"` (no value). Body keeps the code.
- **Acceptance**: `rg 'verification code: {' Kor.Inspections.App/Services/ProjectBootstrapVerificationService.cs` returns nothing. Existing tests pass.
- **Risk**: none.

### 13. [ ] Verification email body doesn't HTML-encode interpolations
- **Files**: `Kor.Inspections.App/Services/ProjectBootstrapVerificationService.cs:143-147`
- **Problem**: Interpolates `{code}` and `{normalizedProject}` raw into HTML. Not exploitable today but breaks the "always encode" pattern used elsewhere.
- **Fix**: wrap both in `WebUtility.HtmlEncode`. Add `using System.Net;` if needed.
- **Acceptance**: builds, tests pass, encoded output for any non-ASCII or HTML-special test input.
- **Risk**: none.

### 14. [ ] Stray code-review markers / mojibake in committed source
- **Files**:
  - `Kor.Inspections.App/Services/BookingService.cs:1` — `// CODEX TEST  verified update`
  - `Kor.Inspections.App/Services/BookingService.cs:599` — `// ADD THIS BLOCK`
  - `Kor.Inspections.App/Services/BookingService.cs:646` — `// ✅ ADD THIS`
  - `Kor.Inspections.App/Pages/Admin/Index.cshtml.cs:89` — `// CRITICAL � used for Anytime pills` (contains `�` replacement char)
- **Fix**: delete the three stray marker lines. Fix or remove the encoding-damaged comment at `Index.cshtml.cs:89`.
- **Acceptance**: `rg -n '(CODEX TEST|ADD THIS BLOCK|✅ ADD THIS|�)' Kor.Inspections.App` returns nothing. Builds clean.
- **Risk**: none.

### 15. [ ] Admin Summary bulk email handler bypasses shared error helper
- **Files**: `Kor.Inspections.App/Pages/Admin/Summary.cshtml.cs:228-297` vs `369-392` (`TrySendSummaryEmailAsync`)
- **Problem**: single-recipient paths use `TrySendSummaryEmailAsync`. Bulk path (`OnPostEmailAllInspectorsAsync`) has its own try/catch with a different log template.
- **Fix**: refactor bulk path to call `TrySendSummaryEmailAsync` per recipient. Accumulate sent/failed from its boolean return.
- **Acceptance**: `SummaryModelEmailTests.OnPostEmailAllInspectorsAsync_WhenOneMailFails_ReportsSentAndFailedRecipients` passes unchanged (may need minor assertion tweak).
- **Risk**: low.

### 16. [ ] Disabled-inspector email-recipient policy inconsistent — NEEDS USER DECISION
- **Files**: `Kor.Inspections.App/Services/BookingService.cs:390, 428` vs `Kor.Inspections.App/Pages/Admin/Summary.cshtml.cs:201`
- **Problem**: BookingService cancellation uses `requireEnabled: false` (disabled still notified). Admin summary filters `i.Enabled == true` (disabled skipped).
- **Question for user**: pick policy. **(A)** Disabled = fully off the mailing list (change cancellation to `requireEnabled: true`). **(B)** Disabled = historical assignments still notified (drop `i.Enabled` filter in summary).
- **Recommended**: **(A)**.
- **Risk**: depends on choice.

### 17. [ ] Dead `ProjectAccessService` + `ProjectAccess` table
- **Files**:
  - `Kor.Inspections.App/Services/ProjectAccessService.cs`
  - `Kor.Inspections.App/Data/Models/ProjectAccess.cs`
  - `Kor.Inspections.App/Data/InspectionsContext.cs:17, 132-154`
  - `Kor.Inspections.Tests/Services/ProjectAccessServiceTests.cs`
- **Problem**: Legacy PIN service never DI-registered. Only consumer is its own test. Table exists in prod.
- **Fix**: delete service, model, DbSet, test file. Generate migration `RemoveProjectAccessTable` that drops the table.
- **Acceptance**: builds, `dotnet test` passes with 5 fewer tests. `rg ProjectAccess Kor.Inspections.App` returns nothing.
- **Risk**: migration drops a prod table. Confirm with user nobody has been using PIN auth via direct SQL first.

### 18. [ ] `OnPostSaveContactAjaxAsync` maps all `InvalidOperationException` to "already exists"
- **Files**: `Kor.Inspections.App/Pages/Index.cshtml.cs:614-618`, `Kor.Inspections.App/Services/ProjectProfileService.cs:131, 138-139, 152-153, 193-195`
- **Problem**: service throws `InvalidOperationException` for four distinct cases. Handler maps all to "already exists or updated by another user" — misleading for three of them.
- **Fix**: introduce exception types (`ContactNotFoundException`, `InvalidContactEmailException`, `ContactAlreadyExistsException`) in ProjectProfileService. Handler catches each and maps to specific message.
- **Acceptance**: pre-existing "duplicate email" behavior unchanged. Submit with missing email → 400 with correct message.
- **Risk**: low.

### 19. [ ] Concurrent `SendCodeAsync` races on unique index
- **Files**: `Kor.Inspections.App/Services/ProjectBootstrapVerificationService.cs:76-167`
- **Problem**: two simultaneous "send code" clicks for (project, email) race on insert. Second hits `IX_ProjectVerifications_ProjectEmail` → unhandled `DbUpdateException` → 500.
- **Fix**: catch `DbUpdateException` on the unique-index violation, retry the find-and-update path once.
- **Acceptance**: new test: two parallel `SendCodeAsync` tasks for same (project, email) both complete without throw; exactly one row persisted.
- **Risk**: low.

### 20. [ ] `PersistRouteOrderAsync` inspector email match case-sensitive
- **Files**: `Kor.Inspections.App/Pages/Admin/Summary.cshtml.cs:515`
- **Problem**: `b.AssignedTo == request.InspectorEmail` exact-match. Collation-dependent. Related to #3.
- **Depends on**: #3. Covered by the sweep.

---

## 🟢 P3 — Operational / nice-to-have

### 21. [ ] `/healthz` requires OIDC → no automated probe can reach it
- **Files**: `Kor.Inspections.App/Program.cs:33-37, 202-204`
- **Problem**: only auth scheme is OIDC → probe gets 302 to Microsoft login.
- **Fix**: register a second scheme (API-key via `X-Health-Probe-Key` header, value from `Health:ProbeKey` env var). Policy accepts either. Replace `AddDbContextCheck` with `AddSqlServer(connectionString, healthQuery: "SELECT 1")`.
- **Acceptance**: `HealthzEndpointTests` updated: valid key → 200, invalid → 401.
- **Risk**: medium — touches startup auth.

### 22. [ ] `NotificationOptions.Email` is ambiguous with `FromMailbox`
- **Fix**: rename `Email` → `AdminRecipientEmail`. Update `appsettings.json`, `appsettings.Production.json` (if it has an override), and 4-5 call sites.
- **Risk**: low. Pure rename.

### 23. [ ] Consolidate duplicated `GetExistingBookingsForLocalDateAsync`
- **Files**: `Pages/Index.cshtml.cs:886-897`, `Pages/Admin/Index.cshtml.cs` (same method)
- **Fix**: promote to `TimeRuleService.GetExistingBookingsForLocalDateAsync(DbContext, DateOnly)`. Both pages call it.

### 24. [ ] Consolidate `SqlServerFixture` across 9 test files
- **Fix**: extract to `Kor.Inspections.Tests/Helpers/SqlServerFixture.cs`.

### 25. [ ] Root-level `tables.csv` + `columns.csv`
- **Problem**: old Deltek schema dumps, not consumed.
- **Fix**: delete, or move to `docs/deltek-schema/`.

### 26. [ ] Two empty + one duplicate historical migrations
- **Files**:
  - `20260210022234_AddUniqueIndex_ProjectContacts_Email.cs` (empty)
  - `20260211081113_SyncTimePreference.cs` (empty)
  - `20260212012729_Fix_TimePreferenceNullable.cs` (duplicate of `20260212123000_MakeTimePreferenceNullable.cs`)
- **Fix**: docs-only. Add one-line comment in each empty `Up()` explaining the no-op. Don't modify bodies.

### 27. [ ] Extract `Pages/Index.cshtml` inline JS (~1000 lines)
- **Fix**: move to `wwwroot/js/booking-page.js`. Largest PR in the list; do last.

### 28. [ ] Dead JS handler in `Admin/Summary.cshtml` for non-existent markup
- **Files**: `Kor.Inspections.App/Pages/Admin/Summary.cshtml:332-358`
- **Fix**: delete the second `<script>` block.

### 29. [ ] Unused vendored libs in `wwwroot/lib/`
- **Fix**: delete `bootstrap`, `jquery`, `jquery-validation`, `jquery-validation-unobtrusive`.

### 30. [ ] Flatpickr CDN unpinned, no SRI
- **Files**: `Pages/Index.cshtml:5, 1318-1320`
- **Fix**: pin version + add SRI hash, or self-host under `wwwroot/lib/flatpickr/`.

### 31. [ ] Publish profile doesn't delete stale files
- **Files**: `Properties/PublishProfiles/FolderProfile.pubxml:5-7`
- **Fix**: set `<DeleteExistingFiles>true</DeleteExistingFiles>`.
- **Risk**: medium operationally. User decision.

### 32. [ ] Committed production secrets (long-standing)
- **Files**: `appsettings.json:3, 8, 14, 18`, `appsettings.Production.json:3`
- **Fix**: rotate every secret. Replace with `__SET_VIA_ENV__` placeholders. Move values to env vars / Key Vault.
- **Risk**: coordinate with ops. Not a code-only PR.

---

## Execution order

Adjust as we go. Initial order:

**Today**: #1 (5-minute fix)

**This week (P1 sweep)**: #2 → #3 → #5 → #4 → #8

**Next week (polish)**: #7 → #6 → #12 → #13 → #14 → #11 → #10 → #15 → #28

**Structural**: #9 → #16 (after user decision) → #17 → #18 → #19 → #20

**Ops / larger**: #21 → #32 (secrets — ops-heavy)

**Code-health cleanup**: #22 → #23 → #24 → #25 → #26 → #29 → #30 → #27 (last — biggest)

Sprint 4 item #31 (publish profile) is its own user decision — flag it but don't bundle into a code PR.
