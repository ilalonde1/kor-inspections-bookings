# KOR Inspections Bookings — Production Risk Audit

**Date:** 2026-03-10
**Auditor:** Independent code review (principal engineer level)
**Overall Risk Rating:** HIGH (one issue in the Severe category — live credentials in version control)
**Confidence:** High — all critical findings are confirmed from direct code inspection, not inferred from patterns.

---

## Architecture Summary

Single-process ASP.NET Core 8 Razor Pages application on what appears to be a single IIS host. Two trust surfaces: (1) public, unauthenticated booking/manage/verification flow, and (2) admin surface under `/Admin` protected by Entra ID OIDC (`AuthorizeFolder("/Admin")`). Outbound email via Microsoft Graph app credential (client_credentials flow). External project data via synchronous ODBC against Deltek. Application data in SQL Server via EF Core. Project domain verification state held in in-process `IMemoryCache`. No distributed cache, no Redis, no outbox pattern for email.

---

## Top 5 Concerns

1. **Four live production credentials committed to version control** — SQL password, Deltek ODBC password, two Azure AD client secrets in `appsettings.json`, plus a third production client secret in `appsettings.Production.json`. Neither file is in `.gitignore`.
2. **Domain-level verification grants permanent, company-wide access** — one OTP verification by any `@acme.com` employee on project `30844` permanently and irrevocably unlocks read access, contact mutation, and cancellation for every other `@acme.com` user on that project, forever.
3. **Manage page token cancellation produces no audit record** — the most commonly used public cancellation path (email link → Manage page) is completely invisible in `BookingActions`.
4. **Rate limiting is keyed to raw `RemoteIpAddress` with no `UseForwardedHeaders()` middleware** — behind any reverse proxy, all clients share one rate-limit bucket; the protection is simultaneously over-broad for legitimate users and trivially bypassed by an attacker rotating IPs.
5. **TOCTOU race on Manage page POST** — two concurrent cancel requests both pass the already-cancelled check, both write `Status = "Cancelled"`, and both invoke `SendCancellationEmailsAsync()`.

---

## Critical Findings

---

### CRIT-1 — Live Production Credentials Committed to Version-Controlled Configuration

**Severity:** Critical
**Confidence:** Confirmed
**Files:** `Kor.Inspections.App/appsettings.json` (lines 2–18), `Kor.Inspections.App/appsettings.Production.json` (lines 2–4)

**Problem:**
Five distinct live credentials are committed to the repository in plaintext. The root `.gitignore` excludes `appsettings.Development.json` and `appsettings.Local.json` but does **not** exclude `appsettings.json` or `appsettings.Production.json`. Both files are tracked by git.

**Evidence:**
- `appsettings.json` line 4: `"Password=ChangeThisStrongPassword!2025"` — SQL Server account `transmittals_app` on `KOR-APP01`
- `appsettings.json` line 8: `"ClientSecret": "lHV8Q~AcPYpV69rFAThwK9uuqYqcARD_aJmSIbpw"` — Graph app (ClientId `5b20a407-0b59-4c75-b2e5-d2cf970c5dbd`)
- `appsettings.json` line 14: `"ClientSecret": "dV88Q~JA6hvOsjFGi0ixJrjSUQibQR0qimSv5dly"` — Web/AzureAd app (ClientId `c83879a2-9590-4d7d-9ab0-4efc6dcf519f`)
- `appsettings.json` line 18: `"OdbcDsn": "DSN=Deltek;UID=52267.nucleus.prd;PWD=SSgdmOkSR6p9Gf;"` — Deltek operational account
- `appsettings.Production.json` line 3: `"ClientSecret": "2Mn8Q~9j0XkVuCWCFC_InJd5hz0fCnOXumN_BaAJ"` — a *third*, distinct AzureAd client secret (the production credential, separate from the base file value)

The `ValidateRequiredSecret()` check in `Program.cs` (lines 191–198) guards against `"__SET_"` placeholder values at startup. This protection is entirely irrelevant once actual secrets are present in the file.

**Why it matters:**
If this repository has ever been pushed to any remote (GitHub, Azure DevOps, etc.), all five credentials must be treated as already compromised. The Graph client secret allows an attacker to authenticate as the app and send email from `reviews@korstructural.com` via the Microsoft Graph API, and to access any Entra ID resource granted to that app registration. The AzureAd secret allows impersonation of the web application identity to Entra ID. The SQL credential allows direct database access (read/write) to all booking, contact, and access data. The Deltek ODBC credential provides access to the Deltek ERP system.

**Real-world scenario:**
A developer clones the repo for onboarding. The repository has been on GitHub for three months. A credential scanner harvested `lHV8Q~AcPYpV69rFAThwK9uuqYqcARD_aJmSIbpw` within minutes of the first push. The attacker has been quietly sending phishing emails from `reviews@korstructural.com` and reading all booking data from the SQL Server for months.

**Recommended fix direction:**
1. Rotate all five credentials immediately before any other work. Assume the current values are compromised.
2. Add `appsettings.json` and `appsettings.Production.json` to `.gitignore` at the repository root.
3. Move all secrets to environment variables (IIS application pool environment, or `web.config` `environmentVariables` section which is not tracked). For production, use Azure Key Vault references or DPAPI-protected User Secrets.
4. Rewrite the base `appsettings.json` to use `__SET_` placeholder strings so the existing startup validation catches any misconfiguration.
5. Audit the full `git log` for any additional secrets committed in prior commits. Git history persists even after files are changed; `git filter-repo` or BFG Repo Cleaner is required to purge historical occurrences.

---

### CRIT-2 — Manage Page Token Cancellation Produces No Audit Record

**Severity:** Critical
**Confidence:** Confirmed
**Files:** `Kor.Inspections.App/Pages/Manage.cshtml.cs` (lines 48–80), `Kor.Inspections.App/Services/BookingService.cs` (lines 167–188), `Kor.Inspections.App/Pages/Index.cshtml.cs` (lines 466–473)

**Problem:**
The three cancellation paths have inconsistent audit behavior. `ManageModel.OnPostAsync()` directly mutates booking status and calls `SendCancellationEmailsAsync()` but never writes a `BookingAction` row. The other two paths both do. `ManageModel` does not call `BookingService.CancelBookingByTokenAsync()` — it duplicates the cancellation logic inline and omits the audit step.

**Evidence:**
- `ManageModel.OnPostAsync()` lines 71–78: `booking.Status = "Cancelled"; await _db.SaveChangesAsync(); await _bookingService.SendCancellationEmailsAsync(booking);` — no `RecordAction()` or `_db.BookingActions.Add()` call anywhere in this method.
- `BookingService.CancelBookingByTokenAsync()` lines 178–181: `RecordAction(booking.BookingId, "Cancelled", "client-token")` — this method exists but is **never called** by `ManageModel`.
- `IndexModel.OnPostCancelInspectionAsync()` lines 466–473: Explicitly adds a `BookingAction` with the requestor's email as `PerformedBy`.
- `Admin/Index.cshtml.cs` `OnPostCancelAsync()` lines 228–234: Explicitly adds a `BookingAction` with `User.Identity?.Name` as `PerformedBy`.

**Why it matters:**
Client-initiated cancellations via the Manage page URL — the path triggered by clicking the link in every booking confirmation email — leave no trace in `BookingActions`. Any audit query asking "who cancelled this booking and when?" will silently return nothing for the vast majority of cancellations. Future compliance, billing dispute, or investigation logic built on the audit table will be systematically wrong.

**Real-world scenario:**
A customer calls to dispute a cancellation. KOR queries `BookingActions` and finds no record. There is no way to determine whether the booking was cancelled by the client via email link, by an admin, or by a project-scoped cancellation. The audit trail is incomplete for the most common cancellation path.

**Recommended fix direction:**
`ManageModel.OnPostAsync()` should call `BookingService.CancelBookingByTokenAsync(Token)` instead of duplicating the logic inline. That method already handles the guard, the status update, the audit record, and the email send. The inline duplication should be removed entirely.

---

## High Findings

---

### HIGH-1 — Domain-Level Verification Grants Permanent Company-Wide Access

**Severity:** High
**Confidence:** Confirmed
**Files:** `Kor.Inspections.App/Services/ProjectBootstrapVerificationService.cs` (lines 62–91, 208–230, 275–283), `Kor.Inspections.App/Pages/Index.cshtml.cs` (lines 333–391, 394–477)

**Problem:**
Verification is keyed to `(projectNumber, emailDomain)`, not to a specific email address. One successful OTP verification by any `@acme.com` employee for project `30844` immediately writes a `ProjectDefaults` row for `("30844", "acme.com")`. From that point on, `GetStatusAsync()` returns `IsVerified = true` for every future request from any `@acme.com` address for that project, permanently, with no expiry and no admin review step.

**Evidence:**
- `BuildKey()` at lines 275–283: Cache key is `"proj-bootstrap:{projectNumber}|{domain}"`. The specific user email is not part of the key. Any email from the same domain hits the same key.
- `GetStatusAsync()` at lines 65–68: `_db.ProjectDefaults.AnyAsync(x => x.ProjectNumber == normalizedProject && x.EmailDomain == domain)`. One row for `(project, domain)` grants trust to every email at that domain.
- `VerifyCodeAsync()` at lines 208–230: On success, inserts a `ProjectDefaults` row making domain-level trust permanent in the DB.
- `IndexModel.OnPostLookupInspectionsAsync()` lines 362–366: Uses domain suffix `@acme.com` to return ALL bookings for the project that have any email ending in `@acme.com`. One verified domain member can see every other domain member's bookings, including `ContactEmail`, `ContactName`, `ContactPhone`, and `Comments`.
- `IndexModel.OnPostCancelInspectionAsync()` lines 437–443: Same domain-scoped lookup — any domain member can cancel any other domain member's booking for the project.

**Why it matters:**
Access expansion is silent and permanent. There is no revocation mechanism for `ProjectDefaults` rows in the admin UI. A temporary contractor at `acme.com` verifies once; their email account is later deleted, but the DB row remains. Every future employee at `acme.com` who knows any project number that `acme.com` previously touched has full read/cancel access to all bookings for those projects.

**Real-world scenario:**
A structural engineering firm (`acme.com`) has one junior engineer who books a single inspection. Six months later, any other `@acme.com` employee who types in that project number can view all historical and upcoming bookings for the project, see all saved contacts including personal phone numbers and email addresses, and cancel any inspection — including ones made by colleagues they have never met.

**Recommended fix direction:**
This is a design decision, not an implementation accident, but it must be explicitly documented and accepted. If individual-level accountability is required: change `BuildKey()` to use the full email address, not just the domain, and scope `ProjectDefaults` to email rather than domain. If the domain-level trust policy is intentional: add an admin page to view and revoke `ProjectDefaults` entries, add explicit logging every time domain-level trust grants access to a new email address, and ensure the policy is communicated to clients.

---

### HIGH-2 — Double-Cancel TOCTOU Race on Manage Page

**Severity:** High
**Confidence:** Confirmed
**Files:** `Kor.Inspections.App/Pages/Manage.cshtml.cs` (lines 48–79)

**Problem:**
`ManageModel.OnPostAsync()` reads the booking, checks if already cancelled, sets status, saves, and sends emails with no row-level lock, no EF Core concurrency token, and no transaction wrapping the check-and-update sequence. Two concurrent POST requests with the same token both pass the already-cancelled guard, both set `Status = "Cancelled"`, both call `SaveChangesAsync()` successfully (EF generates `UPDATE WHERE BookingId = @id` with no expected-prior-status clause), and both invoke `SendCancellationEmailsAsync()`.

**Evidence:**
```csharp
// ManageModel.OnPostAsync(), lines 50–79
var booking = await _db.Bookings.SingleOrDefaultAsync(b => b.CancelToken == Token); // no lock
if (string.Equals(booking.Status, "Cancelled", ...)) { AlreadyCancelled = true; return; } // races here
if (!_timeRules.IsCancellationAllowed(booking.StartUtc)) { ... return; }
booking.Status = "Cancelled";
await _db.SaveChangesAsync();           // both concurrent calls succeed
await _bookingService.SendCancellationEmailsAsync(booking); // both send emails
```
EF Core's default `SaveChangesAsync()` generates `UPDATE Bookings SET Status='Cancelled' WHERE BookingId='...'`. This succeeds for both concurrent calls because there is no `WHERE Status != 'Cancelled'` clause.

**Why it matters:**
Inspectors and clients receive two cancellation emails for a single user action. Beyond operational noise, this undermines trust in the notification system.

**Real-world scenario:**
A user on a slow mobile connection double-taps the "Cancel" button before the first response arrives. Two concurrent POST requests hit the server within 100ms. Both succeed. The inspector receives two identical cancellation emails within seconds of each other and calls the office to ask if the system is broken.

**Recommended fix direction:**
Use a conditional SQL update: `UPDATE Bookings SET Status='Cancelled' WHERE BookingId=@id AND Status != 'Cancelled'` and check rows-affected to detect a race loss. In EF Core this can be achieved by adding a `rowversion`/`timestamp` column to `Booking` and enabling optimistic concurrency, so the second `SaveChangesAsync()` throws `DbUpdateConcurrencyException`. Alternatively, perform the status transition inside a transaction with `SELECT ... WITH (UPDLOCK)`.

---

### HIGH-3 — Exact-Duplicate Booking Race Under Concurrent Submissions

**Severity:** High
**Confidence:** Confirmed
**Files:** `Kor.Inspections.App/Pages/Index.cshtml.cs` (lines 793–813), `Kor.Inspections.App/Services/BookingService.cs` (lines 105–131)

**Problem:**
The per-user duplicate booking check (lines 793–803 in `Index.cshtml.cs`) runs outside the Serializable transaction in `CreateBookingAsync`. Two concurrent identical submissions from the same user can both pass the duplicate check window and both succeed in `CreateBookingAsync` — provided the slot has capacity for two bookings (`MaxBookingsPerSlot` is configured as `3` in `appsettings.json`).

**Evidence:**
- `OnPostBookAsync()` line 795: `_db.Bookings.AsNoTracking().AnyAsync(b => b.ProjectNumber == ... && b.StartUtc == startUtc && b.Status != "Cancelled" && b.CreatedUtc >= duplicateCutoffUtc)` — runs without any lock, outside any transaction.
- `CreateBookingAsync()` lines 119–131: Opens a Serializable transaction and checks `overlapCount >= maxBookingsPerSlot`. With `MaxBookingsPerSlot = 3`, a slot with zero existing bookings allows both concurrent submissions through (both see `overlapCount = 0 < 3`).
- The 2-minute `duplicateCutoffUtc` window provides zero protection against two requests racing within the same 100ms window.

**Why it matters:**
The same project and contact email appear twice for the same time slot. The customer receives two confirmation emails. Admin must manually identify and cancel the duplicate. The Serializable isolation correctly prevents capacity overflows but does not prevent exact duplicate creation when capacity remains.

**Real-world scenario:**
A user's browser experiences a network timeout after submitting a booking. The browser retries automatically. Both requests arrive ~50ms apart, both pass the `AnyAsync` duplicate check (neither booking exists yet), and both enter the Serializable transaction where `overlapCount = 0 < 3`. Both bookings are created. The customer gets two confirmation emails and calls asking which one is real.

**Recommended fix direction:**
Either move the per-user duplicate detection into the Serializable transaction in `CreateBookingAsync`, or add a unique partial index on `(ProjectNumber, ContactEmail, StartUtc) WHERE Status != 'Cancelled'` to make the database enforce uniqueness. The DB constraint is the more reliable option.

---

### HIGH-4 — Rate Limiting Keyed to Raw `RemoteIpAddress` Without `UseForwardedHeaders`

**Severity:** High
**Confidence:** Confirmed
**Files:** `Kor.Inspections.App/Program.cs` (lines 111–139, 180, 210–224)

**Problem:**
`GetRateLimitPartitionKey()` falls back to `httpContext.Connection.RemoteIpAddress` for unauthenticated users. `Program.cs` does not call `app.UseForwardedHeaders()` anywhere. In any deployment with a reverse proxy (IIS ARR, Azure App Gateway, nginx), `Connection.RemoteIpAddress` is the proxy's IP address for every client.

**Evidence:**
- `Program.cs` line 222: `var ip = httpContext.Connection.RemoteIpAddress?.ToString(); return ... $"ip:{ip}"`. No header forwarding.
- `Program.cs` line 133: The `contactMutation` policy directly uses `httpContext.Connection.RemoteIpAddress?.ToString()`.
- Full `Program.cs` reviewed: no `app.UseForwardedHeaders()`, no `services.Configure<ForwardedHeadersOptions>()`.

**Two simultaneous failure modes:**
1. **Legitimate users blocked**: All users behind a corporate NAT or office network share one rate-limit bucket. One user exhausting the booking limit (`5 per 30 minutes`) blocks all colleagues at that site.
2. **Attackers bypass trivially**: An attacker using a VPN or rotating proxies gets a fresh rate-limit bucket per exit IP. The protection does not exist against any determined actor.

**Real-world scenario:**
Three engineers at the same client office try to book inspections in the same 30-minute window. The first engineer's 5 requests exhaust the bucket. Engineers 2 and 3 receive `429 Too Many Requests` and cannot book. The booking site appears broken to 2 out of 3 users.

**Recommended fix direction:**
Add `app.UseForwardedHeaders()` before all other middleware, with `ForwardedHeaders.XForwardedFor` enabled and `KnownProxies` or `KnownNetworks` configured to trust only the actual reverse proxy. With correct configuration, `Connection.RemoteIpAddress` will reflect the true client IP after this middleware runs.

---

### HIGH-5 — Manage Page Allows Cancellation of Completed Bookings

**Severity:** High
**Confidence:** Confirmed
**Files:** `Kor.Inspections.App/Pages/Manage.cshtml.cs` (lines 48–79), `Kor.Inspections.App/Pages/Index.cshtml.cs` (lines 453–457)

**Problem:**
`ManageModel.OnPostAsync()` only guards against `Status == "Cancelled"`. It does not guard against `Status == "Completed"`. `IsCancellationAllowed()` checks the booking time relative to the cutoff but does not check status. A booking that has already been marked `"Completed"` but whose time falls within the cutoff window can be cancelled via the Manage page token URL.

**Evidence:**
- `ManageModel.OnPostAsync()` line 58: `if (string.Equals(booking.Status, "Cancelled", ...)) { AlreadyCancelled = true; ... return; }` — only one status check, and it is not for `"Completed"`.
- `IndexModel.OnPostCancelInspectionAsync()` line 454: `if (string.Equals(booking.Status, "Completed", ...)) { Response.StatusCode = 400; return new JsonResult(...); }` — explicitly blocks completed bookings.
- `TimeRuleService.IsCancellationAllowed()` lines 123–146: Checks `bookingLocal <= nowLocal` (past check) and the cutoff day calculation. Returns `true` if the booking time is still in the future and before the cutoff. Status is the caller's responsibility.

**Why it matters:**
A booking marked Completed could be driven back to Cancelled by anyone holding the email-delivered cancel token, generating incorrect cancellation emails for work that has already been done and creating inconsistent records.

**Recommended fix direction:**
Add a `Status == "Completed"` guard immediately after the `Status == "Cancelled"` guard in `ManageModel.OnPostAsync()`, matching the behavior already present in `IndexModel.OnPostCancelInspectionAsync()`.

---

## Medium Findings

---

### MED-1 — MSAL Token Acquisition Creates New App Instance Per Email Send

**Severity:** Medium
**Confidence:** Confirmed
**Files:** `Kor.Inspections.App/Services/GraphMailService.cs` (lines 23–48)

**Problem:**
`GetTokenAsync()` calls `ConfidentialClientApplicationBuilder.Create(...)...Build()` on every invocation. Each call creates a new `ConfidentialClientApplication` with an empty in-memory token cache. MSAL's built-in token caching is scoped to the application instance. Since a new instance is created per call, no cached token is ever reused. Every email send triggers an HTTP round-trip to `login.microsoftonline.com` to acquire a new token.

**Evidence:**
- `GraphMailService.cs` lines 37–45: `var app = ConfidentialClientApplicationBuilder.Create(clientId).WithClientSecret(clientSecret).WithAuthority(...).Build(); var result = await app.AcquireTokenForClient(...).ExecuteAsync();`
- Called from `SendHtmlAsync()` which is invoked for every outbound email. On booking creation: 2 calls (submitter + admin notification). On assignment: 2 calls (client + inspector). On cancellation: 2 calls.

**Why it matters:**
Under normal load this doubles or triples unnecessary latency on every user-facing action that triggers email. Under increased load, repeated token acquisition requests against the AAD token endpoint risk triggering throttling (`429`). Graph `sendMail` failures are logged but not re-thrown for booking emails, so the symptom would be silent email delivery failure.

**Recommended fix direction:**
Make `ConfidentialClientApplication` a singleton (static field or singleton-lifetime DI registration) so MSAL's internal token cache persists across requests and handles expiry and refresh automatically.

---

### MED-2 — Inspector Summary Email Filter Uses Case-Sensitive Comparison

**Severity:** Medium
**Confidence:** Confirmed
**Files:** `Kor.Inspections.App/Pages/Admin/Summary.cshtml.cs` (lines 172–177)

**Problem:**
`OnPostEmailInspectorAsync()` filters bookings for a specific inspector using `StringComparison.Ordinal` (case-sensitive). The `b.AssignedTo` field in `SummaryRow` is the display name resolved through a dictionary built with `StringComparer.OrdinalIgnoreCase`. The comparison against `inspector.DisplayName` is case-sensitive. Any casing discrepancy between the stored display name and the resolved value causes the filter to return zero bookings silently.

**Evidence:**
- Line 176: `string.Equals(b.AssignedTo, inspector.DisplayName, StringComparison.Ordinal)` — case-sensitive.
- `ResolveAssignedToDisplay()` lines 300–309: Dictionary lookup uses `StringComparer.OrdinalIgnoreCase`.
- `OnPostEmailAllInspectorsAsync()` lines 200–210: Groups by `b.AssignedTo` using `StringComparer.OrdinalIgnoreCase` — inconsistent with the single-inspector path in the same file.

**Why it matters:**
An inspector silently receives no summary email if their display name has any casing variation. The admin who clicks "Send Inspector Summary" sees `StatusMessage = "No bookings found for that inspector."` and has no way to distinguish between "the inspector has no bookings" and "the string comparison failed." This is a silent reliability failure.

**Recommended fix direction:**
Change `StringComparison.Ordinal` to `StringComparison.OrdinalIgnoreCase` on line 176 to match the rest of the file.

---

### MED-3 — Cancel Token Never Expires — Persistent Attack Surface

**Severity:** Medium
**Confidence:** Confirmed
**Files:** `Kor.Inspections.App/Data/Models/Booking.cs` (line 43), `Kor.Inspections.App/Pages/Manage.cshtml.cs` (lines 27, 82–118)

**Problem:**
`CancelToken` is a `GUID` generated by SQL (`NEWID()`) on insert and never rotated or invalidated after the booking is completed, cancelled, or past the cancellation window. The token is embedded in email confirmation links. The Manage page `LoadAsync()` renders project number, local date/time, status, and assigned inspector display name for any valid token, with no expiry check.

**Evidence:**
- `Manage.cshtml.cs` line 27: `[BindProperty(SupportsGet = true)] public Guid Token { get; set; }` — GET with token renders full booking details.
- `LoadAsync()` lines 82–118: Renders `ProjectNumber`, `LocalDateText`, `LocalTimeText`, `StatusText`, `AssignedTo` with no time-based or status-based expiry guard.
- `BuildManageUrl()` in `BookingService.cs` lines 387–394: URL is static; the token never changes regardless of booking lifecycle state.

**Why it matters:**
Email archives (personal inbox, corporate mail archive, email security gateways) contain valid manage links for all historical bookings indefinitely. Anyone who gains read access to an inbox — whether through credential phishing, account compromise, or a departing employee's email archive — can view or attempt to cancel historical bookings for all projects that email address has ever touched.

**Recommended fix direction:**
After a booking reaches a terminal state (Completed or Cancelled) or after the cancellation window closes, `ManageModel.OnGetAsync()` should return a minimal view without exposing booking details (e.g., "This booking is no longer active"). Alternatively, add a `CancelTokenExpiresUtc` column set to the cancellation cutoff datetime, and gate both GET and POST on that column.

---

### MED-4 — LIKE Wildcard Injection in Domain-Based Booking Lookup

**Severity:** Medium
**Confidence:** Confirmed (exploitability gated; injection mechanism is confirmed)
**Files:** `Kor.Inspections.App/Pages/Index.cshtml.cs` (lines 354–366, 420–443)

**Problem:**
The `domainSuffix` used in `EF.Functions.Like()` is constructed directly from user-supplied email input without escaping SQL LIKE wildcards (`%`, `_`, `[`). An attacker who controls the email input can craft a pattern that matches unintended rows.

**Evidence:**
- `OnPostLookupInspectionsAsync()` line 354: `var domain = emailRaw[(at + 1)..].Trim().ToLowerInvariant();`
- Line 360–365: `var domainSuffix = "@" + domain;` then `EF.Functions.Like(b.ContactEmail, "%" + domainSuffix)`. If `domain = "%"`, the pattern becomes `%@%` which matches every row in `Bookings` that contains `@` in `ContactEmail`.
- `OnPostCancelInspectionAsync()` lines 427–443: Identical pattern.

**Current exploitability:** Gated behind `EnsureVerifiedForProjectAccessAsync()`. To achieve verification with domain `%`, an attacker must receive an OTP email at an address with domain `%`. This is not a valid email domain and delivery would fail, causing `SendCodeAsync()` to return `false`. In practice, the injection is not exploitable for unauthorized cross-domain data access under the current verification model. However, the injection mechanism is structurally present and becomes exploitable if the verification gate is ever weakened.

**Recommended fix direction:**
Escape LIKE special characters (`%`, `_`, `[`, `]`) from the domain string before constructing the pattern. Alternatively, use a parameterized `EndsWith` approach or add a DB-level check constraint on `ContactEmail` format.

---

### MED-5 — PIN Storage Uses SHA-256 Without Salt or Work Factor

**Severity:** Medium
**Confidence:** Confirmed
**Files:** `Kor.Inspections.App/Services/ProjectAccessService.cs` (lines 120–131)

**Problem:**
`HashPin()` computes `SHA256(UTF-16LE bytes of pin.Trim())` with no salt and no work factor. SHA-256 of a short numeric PIN is reversible in microseconds with a precomputed table covering all possible values.

**Evidence:**
- Lines 121–131: `using var sha = SHA256.Create(); var bytes = Encoding.Unicode.GetBytes(pin.Trim()); return sha.ComputeHash(bytes);`
- No salt. No PBKDF2, BCrypt, Argon2, or any password-appropriate KDF.
- A 6-digit numeric PIN has 1,000,000 possible values. At ~10 billion SHA-256 operations per second on commodity hardware, the entire space is computed in under 1 millisecond.

**Current scope:** `ProjectAccessService` is registered in the test project but no page handler in the reviewed codebase calls `ValidatePinAsync()`. If this service is not currently wired to a public-facing endpoint, the risk is contained. If it is wired up anywhere not found in this review, this is a high-severity issue.

**Recommended fix direction:**
Replace SHA-256 with `Rfc2898DeriveBytes` (PBKDF2-SHA256, 600,000+ iterations per NIST SP 800-132) or BCrypt. Store a per-PIN random salt alongside the hash.

---

### MED-6 — `AllowedHosts: "*"` Disables Host Header Filtering

**Severity:** Medium
**Confidence:** Confirmed
**Files:** `Kor.Inspections.App/appsettings.json` (line 77)

**Problem:**
`"AllowedHosts": "*"` disables ASP.NET Core's `HostFilteringMiddleware`. Any `Host:` header value is accepted. Link generation that derives URLs from `HttpContext.Request.Host` (open redirect via crafted Host header), and CSRF token validation that uses host-bound expectations, both become weaker.

**Evidence:**
- `appsettings.json` line 77: `"AllowedHosts": "*"`.
- `BuildManageUrl()` and `BuildProjectInspectionsUrl()` in `BookingService.cs` use `_appOptions.PublicBaseUrl` (configuration-derived), not `Request.Host`. Actual exploitation is limited for the current link-building code.
- OpenID Connect redirect validation is partially handled by the OIDC middleware's `CallbackPath` and redirect URI validation, but host header injection can interfere with server-originated redirects.

**Recommended fix direction:**
Set `"AllowedHosts": "bookings.korstructural.com"` (the actual production hostname). In development, override via `appsettings.Development.json` (which is gitignored).

---

## Low Findings

---

### LOW-1 — ODBC Operations Ignore CancellationToken Within Blocking Scope

**Severity:** Low
**Confidence:** Confirmed
**Files:** `Kor.Inspections.App/Services/DeltekProjectService.cs` (lines 56–87, 107–163)

**Problem:**
Both `GetProjectByNumberAsync()` and `SearchProjectsAsync()` wrap synchronous ODBC operations in `Task.Run(..., ct)`. The `ct` is passed to `Task.Run` to cancel scheduling, but once the lambda begins executing, `conn.Open()`, `cmd.ExecuteReader()`, and all ODBC I/O are synchronous and do not check or respect the cancellation token. Only the read loop in `QueryProjects()` calls `ct.ThrowIfCancellationRequested()`. No `OdbcCommand.CommandTimeout` is set.

**Why it matters:**
A slow or unreachable Deltek connection holds a thread-pool thread for the duration of the OS-level connection timeout (typically 30–60 seconds). Under moderate load with a degraded Deltek connection, thread exhaustion causes cascading failures across the application.

**Recommended fix direction:**
Set `cmd.CommandTimeout` to a short value (e.g., 10 seconds). Register a `CancellationToken.Register` callback that calls `cmd.Cancel()` if the token is cancelled before the ODBC call completes.

---

### LOW-2 — In-Process MemoryCache for Verification State — Multi-Instance Risk

**Severity:** Low
**Confidence:** Inference
**Files:** `Kor.Inspections.App/Services/ProjectBootstrapVerificationService.cs` (lines 17–18, 109–133), `Kor.Inspections.App/Program.cs` (line 34)

**Problem:**
`IMemoryCache` is in-process. If the application ever runs on more than one instance (IIS with multiple worker processes, Azure App Service scaled to 2+ instances, NLB), verification state is not shared. A user who verifies on instance A will be prompted to re-verify on instance B.

**Current mitigation:** After the first successful verification, a `ProjectDefaults` row is written to the DB. Subsequent requests from the same domain check the DB first (`GetStatusAsync()` lines 65–68) and find the row, granting trust regardless of which instance handles the request. The only window of failure is between verification and the next request if they hit different instances and the DB write has not yet occurred.

**Current risk level:** Low — likely a single-IIS deployment. Becomes High if the deployment model changes.

**Recommended fix direction:**
Document the single-instance assumption. If horizontal scaling is ever required, replace `IMemoryCache` with `IDistributedCache` (Redis or SQL-backed) for the verification state.

---

### LOW-3 — `ElementsJson` Dead Field in Booking Model

**Severity:** Low
**Confidence:** Inference
**Files:** `Kor.Inspections.App/Data/Models/Booking.cs` (line 14)

**Problem:**
`public string? ElementsJson { get; set; }` is declared on `Booking` but is never populated by `CreateBookingAsync()`, any page handler, or any migration script. It is not surfaced in any email template or admin view.

**Why it matters:**
Dead fields in the domain model create confusion about intent. If this field was removed from the booking form without a corresponding schema cleanup, old code that might have relied on it could silently write `null` where structured data was expected.

**Recommended fix direction:**
If unused, create a migration to drop the column and remove the property from the model. If it represents a future feature, add a comment documenting that intent.

---

### LOW-4 — Inspector Lookup Matches by DisplayName — Potential Collision Risk

**Severity:** Low
**Confidence:** Inference
**Files:** `Kor.Inspections.App/Services/BookingService.cs` (lines 60–73)

**Problem:**
`GetAssignedInspectorAsync()` matches inspectors by `i.Email == assignedTo || i.DisplayName == assignedTo`. The `Inspectors` table has no unique constraint on `DisplayName`. If two inspectors share the same display name (or if `AssignedTo` was stored as a display name rather than an email through a manual DB edit), the lookup returns an unpredictable result.

**Current risk level:** Low — in practice `AssignedTo` is always set to `inspector.Email` by `OnPostAssignAsync()`, so the display name branch is defensive code. The risk materializes only with manual DB modifications.

**Recommended fix direction:**
Add a unique index on `Inspectors.Email`. Add a unique index on `Inspectors.DisplayName` if display name uniqueness is a business requirement.

---

## Confirmed Strengths

- **Serializable transaction for booking creation** (`BookingService.cs` line 105): The authoritative slot-capacity check runs inside a `Serializable` isolation transaction. This correctly prevents phantom reads and is the right, uncommon choice for preventing double-booking beyond capacity under concurrent load.
- **HTML encoding in all email builders**: Every user-supplied field in all email templates (`BuildDetailedBookingHtml`, `BuildAssignmentEmailHtml`, `BuildCancellationEmailHtml`, `BuildEmailHtml` in Summary) is wrapped in `WebUtility.HtmlEncode()`. No XSS attack surface exists in outbound email.
- **ODBC parameterization**: `DeltekProjectService.AddParametersForPlaceholders()` counts `?` placeholder characters and adds `OdbcCommand.Parameters.AddWithValue()` entries. Queries are parameterized. No SQL injection via ODBC.
- **Constant-time PIN comparison** (`ProjectAccessService.cs` lines 133–143): `ConstantTimeEquals()` is correctly implemented as a bitwise OR accumulation of XOR differences across all bytes. Timing side-channel attacks against PIN validation are mitigated.
- **Production startup validation** (`Program.cs` lines 144–157): Secrets are validated at startup in `IsProduction()` with explicit `throw`. The application will not silently start with placeholder credentials. `TimeZoneId` is validated by attempting `TimeZoneInfo.FindSystemTimeZoneById()` and catching both exception types.
- **Honeypot bot protection** (`IndexModel.CompanyFax`): Simple, present, and functional. Automated form submissions that populate all fields are silently redirected to `/Confirm` without creating a booking.
- **Project profile service domain scoping** (`ProjectProfileService.cs`): All contact and default queries include both `projectNumber` AND `emailDomain` as required filter conditions. A domain can only see and mutate its own contacts; contacts from another domain are structurally invisible.
- **Disabled inspector handling**: `SendAssignmentEmailAsync()` uses `requireEnabled: true` and will not send to a disabled inspector. `SendCancellationEmailsAsync()` correctly uses `requireEnabled: false` to notify an inspector who was assigned at the time of booking even if they have since been disabled.
- **Admin-only actions protected by AuthorizeFolder**: `Program.cs` line 55: `options.Conventions.AuthorizeFolder("/Admin")`. All pages under `/Admin` require Entra ID authentication.

---

## Open Questions

1. **Is the admin page verified as requiring authentication in production?** The e2e test `booking-core.spec.ts` lines 14–21 navigates to `/admin` without a visible login step and asserts the "Field Reviews" heading is visible. The gitignored `tests/e2e/storageState.json` suggests pre-authenticated state is used. This must be manually verified in production: browse to `https://bookings.korstructural.com/admin` in a private browser window and confirm redirect to Microsoft login.

2. **Is `ProjectAccessService.ValidatePinAsync()` wired to any live page handler?** This service is tested in `ProjectAccessServiceTests.cs` but no page model in the reviewed codebase calls it. If there is a PIN-based access page accessible from a URL not found in this review, it is a high-severity brute-force vector (no rate limiting on the PIN path was found). Confirm by searching for all `ProjectAccessService` usages.

3. **What is `ElementsJson` on `Booking` used for?** It is declared in the model but never populated in any page handler or service found in this review. Is it a dead field from a prior feature, or is it populated via a path not reviewed?

4. **Is the deployment single-instance?** The in-process `IMemoryCache` for verification state is correct for single-instance. If the IIS Application Pool runs multiple worker processes or if the app is ever deployed with load balancing, verification state will not be shared across instances. Needs confirmation from the infrastructure owner.

5. **Are there any admin-scoped endpoints or page handlers outside the `/Admin` folder?** The convention is `AuthorizeFolder("/Admin")`. Any page handler added outside this folder in the future would be public. This must be a documented architectural rule.

6. **What permissions does the Deltek ODBC account `52267.nucleus.prd` have?** The queries are read-only (`SELECT`), but if the account has write permissions to Deltek tables, a compromised credential could corrupt ERP data. Validate the account has only `SELECT` on the tables required by the configured queries.

7. **Has the git history been scanned for additional secrets?** The five secrets found in the current working tree may not be the only ones. Earlier commits may contain additional or different credentials that were added and later removed from files but remain in git history.

---

## Test Gaps

| Risk Area | Current Coverage |
|---|---|
| Booking concurrency / double-booking under Serializable isolation | **None** |
| Double-cancel TOCTOU race | **None** |
| Verification bypass / domain-level trust expansion | **None** |
| Completed booking cancelled via Manage page | **None** |
| Admin cancellation of completed booking | **None** |
| Cancel token exposure via GET (unauthenticated info disclosure) | **None** |
| `OnPostCancelInspectionAsync` without a verified domain | **None** |
| Rate limit enforcement under concurrent requests | **None** |
| GraphMailService failure: booking succeeds, email fails | **None** |
| LIKE wildcard injection in domain suffix | **None** |
| Deltek returning null/missing fields | **None** |
| ProjectAccessService PIN brute-force (no rate limit) | **None** |
| Inspector summary email with Ordinal casing mismatch | **None** |
| Manage page cancellation audit record | **None** |
| E2E: full booking flow including verification step | **None** (e2e only checks page presence) |
| E2E: authenticated admin actions (assign, cancel) | **None** |
| Time rule boundary cases (cutoff hour, padding) | **Solid** |
| Slot count with no bookings | **Present** |
| Travel padding blocks adjacent slots | **Present** |
| Cancellation allowed before/after cutoff | **Present** |
| Phone normalizer | **Present** |
| PIN validation (ProjectAccess) | **Present** (but tests a service not wired to any handler) |

The two e2e test files (`booking-core.spec.ts`, `admin-mobile-inspector.spec.ts`) check that elements exist on pages. They do not submit a booking, trigger or complete verification, attempt a cancellation, exercise any admin action, or validate any business rule. They are page-presence smoke tests, not behavior validation.

---

## Recommended Remediation Order

| Priority | Finding | Action |
|---|---|---|
| P0 | CRIT-1 | Rotate all five credentials immediately |
| P0 | CRIT-1 | Remove secrets from config files; move to environment variables |
| P0 | CRIT-1 | Add `appsettings.json` and `appsettings.Production.json` to `.gitignore` |
| P1 | CRIT-2 | Add `BookingAction` record to `ManageModel.OnPostAsync()` |
| P1 | HIGH-4 | Add `UseForwardedHeaders()` middleware with trusted proxy configuration |
| P1 | HIGH-5 | Add `Status == "Completed"` guard to `ManageModel.OnPostAsync()` |
| P1 | HIGH-2 | Fix double-cancel race — add optimistic concurrency or conditional UPDATE |
| P1 | HIGH-3 | Add unique partial DB index on `(ProjectNumber, ContactEmail, StartUtc) WHERE Status != 'Cancelled'` |
| P2 | HIGH-1 | Document domain-level trust model; add admin revocation UI for `ProjectDefaults` |
| P2 | MED-1 | Singleton MSAL app in `GraphMailService` |
| P2 | MED-2 | Fix `StringComparison.Ordinal` → `OrdinalIgnoreCase` in `OnPostEmailInspectorAsync` |
| P2 | MED-3 | Gate Manage page GET on terminal-state check to limit cancel token exposure |
| P2 | MED-6 | Set `AllowedHosts` to the actual production hostname |
| P3 | MED-4 | Escape LIKE wildcards in domain suffix construction |
| P3 | MED-5 | Replace SHA-256 PIN hashing with PBKDF2 or BCrypt |
| P3 | LOW-1 | Set `OdbcCommand.CommandTimeout`; register cancellation callback |
| P3 | LOW-3 | Remove `ElementsJson` or document its intended use |
| P3 | LOW-4 | Add unique index on `Inspectors.Email` |

---

## Immediate Actions (Next 24 Hours)

> **Stop all other development work until these steps are complete.**
> The repository contains live production credentials. Assume all five are already compromised
> if the repository has ever been pushed to any remote.

1. **Revoke the Graph app client secret** (ClientId `5b20a407-0b59-4c75-b2e5-d2cf970c5dbd`, secret `lHV8Q~AcPYpV69rFAThwK9uuqYqcARD_aJmSIbpw`) in the Azure portal → App registrations → Certificates & secrets.
2. **Revoke the Web/AzureAd app client secret** (ClientId `c83879a2-9590-4d7d-9ab0-4efc6dcf519f`, secrets `dV88Q~JA6hvOsjFGi0ixJrjSUQibQR0qimSv5dly` and `2Mn8Q~9j0XkVuCWCFC_InJd5hz0fCnOXumN_BaAJ`). Both values must be revoked.
3. **Reset the SQL Server login password** for account `transmittals_app` on `KOR-APP01`.
4. **Reset the Deltek ODBC credentials** for account `52267.nucleus.prd`.
5. **Generate new secrets for all four** (Graph, AzureAd, SQL, Deltek). Use a minimum of 32 random bytes for client secrets.
6. **Deploy the new secrets via IIS environment variables** (Application Pool → Advanced Settings → Environment Variables) or via a secrets manager. Do not write them to any file that is tracked by git.
7. **Add `appsettings.json` and `appsettings.Production.json` to `.gitignore`** at the repository root immediately after the secrets are removed from those files.
8. **Rewrite the secret-bearing entries in `appsettings.json`** to use `__SET_` placeholder strings (e.g., `"ClientSecret": "__SET_GRAPH_CLIENT_SECRET"`). This ensures the existing startup validation catches any future misconfiguration.
9. **Scan the full git history** for additional committed secrets using `git log -p | grep -i "password\|secret\|pwd\|clientsecret"`. If found, run BFG Repo Cleaner or `git filter-repo` to purge the history, then force-push and notify all contributors to re-clone.
10. **Review the Azure AD audit log** (`portal.azure.com` → Azure Active Directory → Audit logs) and the Graph app's sign-in log for any authentication events from unexpected IP addresses or service principals using the compromised client secrets.

---

## Short-Term Actions (Next 7 Days)

1. **Add audit record to `ManageModel.OnPostAsync()`** (CRIT-2). Replace the inline cancellation logic with a call to `BookingService.CancelBookingByTokenAsync(Token)`, or add `_db.BookingActions.Add(...)` before `SaveChangesAsync()`.
2. **Add `app.UseForwardedHeaders()`** to `Program.cs` before `UseRateLimiter()` (HIGH-4). Configure `ForwardedHeadersOptions.KnownProxies` to trust only the actual reverse proxy address.
3. **Add `Status == "Completed"` guard to `ManageModel.OnPostAsync()`** (HIGH-5).
4. **Fix the double-cancel race** on the Manage page by adding a SQL rowversion column to `Booking` and enabling EF Core optimistic concurrency, or by rewriting the status transition as a conditional `UPDATE WHERE Status NOT IN ('Cancelled', 'Completed')` (HIGH-2).
5. **Fix the case-sensitive string comparison** in `SummaryModel.OnPostEmailInspectorAsync()` — change `StringComparison.Ordinal` to `StringComparison.OrdinalIgnoreCase` on line 176 (MED-2).
6. **Make MSAL singleton** in `GraphMailService` (MED-1). Extract the `ConfidentialClientApplication` build to a field or a singleton-lifetime service so the token cache persists.
7. **Set `AllowedHosts`** to `"bookings.korstructural.com"` in `appsettings.json` (MED-6).
8. **Manually verify** that `/admin` requires authentication in production by browsing to it in a private browser window without cookies.
9. **Add a unique partial index** on `Bookings (ProjectNumber, ContactEmail, StartUtc) WHERE Status != 'Cancelled'` to prevent exact-duplicate bookings under concurrent submission (HIGH-3). Add a corresponding migration.
10. **Create a `BookingStatus` enum** or string constant class and replace all inline string literals (`"Cancelled"`, `"Unassigned"`, `"Assigned"`, `"Completed"`) with the constant. Add a check constraint to the DB `Bookings` table for the valid status set.

---

## Follow-Up Actions

1. **Document the domain-level trust policy** in an Architecture Decision Record (ADR). State explicitly whether domain-level trust is an intentional product decision or a security compromise. If intentional, add an admin page to view, search, and revoke `ProjectDefaults` rows. Add logging to `GetStatusAsync()` when domain-level trust grants access to a new email address for the first time.
2. **Build a dead-letter mechanism for email failures**. The current catch-and-log pattern for `SendInitialEmailsAsync`, `SendAssignmentEmailAsync`, and `SendCancellationEmailsAsync` silently drops emails. Add a `PendingEmails` table (outbox pattern) populated inside the booking transaction, and a background job that retries delivery. At minimum, add an alert when three consecutive email sends fail.
3. **Add a health check endpoint** (`/healthz`) that validates DB connectivity, Graph token acquisition, and time zone resolution. Wire it to whatever monitoring or load balancer probes the deployment.
4. **Add integration tests for the three cancellation paths** verifying that each produces a `BookingAction` row with the correct `PerformedBy` and `ActionType` values.
5. **Add a concurrency test** that submits two identical booking requests simultaneously and verifies that only one booking is created.
6. **Add a test for the domain-level trust behavior** verifying that verifying as `alice@acme.com` grants `IsVerified = true` for `bob@acme.com` on the same project — so that the behavior is documented, tested, and any future change to restrict it is explicit.
7. **Add gate-timeout on the Manage page cancel token**: set a terminal state after the cancellation window closes, rendering the token inert for POST requests while still showing a read-only booking summary for transparency.
8. **Migrate `ProjectAccessService` PIN hashing** from SHA-256 to PBKDF2 with a per-PIN salt before activating any PIN-based access flow from a public-facing page handler.
9. **Escape LIKE wildcards in domain suffix** construction in `OnPostLookupInspectionsAsync()` and `OnPostCancelInspectionAsync()` to eliminate the latent injection surface.
10. **Confirm Deltek ODBC account privileges** are limited to `SELECT` only on the required tables. If the account has write access, scope it down to read-only at the database level.
