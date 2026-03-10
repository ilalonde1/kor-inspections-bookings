# KOR Inspections Bookings — Remediation Plan

Generated: 2026-03-09
Source: Static analysis issue report (full codebase review)
Application: `Kor.Inspections.App` — ASP.NET Core 8.0 Razor Pages

---

## Table of Contents

1. [Emergency: First 24 Hours](#emergency-first-24-hours)
2. [Priority Definitions](#priority-definitions)
3. [P0 — Critical / Immediate](#p0--critical--immediate)
4. [P1 — High / Next 7 Days](#p1--high--next-7-days)
5. [P2 — Medium / Next 30 Days](#p2--medium--next-30-days)
6. [P3 — Low / Backlog](#p3--low--backlog)
7. [Section: Immediate Containment Actions](#section-immediate-containment-actions)
8. [Section: Permanent Fixes](#section-permanent-fixes)
9. [Section: Cleanup and Follow-up Work](#section-cleanup-and-follow-up-work)
10. [Timeline Summary](#timeline-summary)
11. [Finding-to-Remediation Mapping Table](#finding-to-remediation-mapping-table)

---

## Emergency: First 24 Hours

> **Stop everything and complete these steps before any other development work.**
> The git repository contains live production credentials. Assume they are already
> compromised if the repo has ever been pushed to any remote (GitHub, Azure DevOps, etc.).

### Step-by-Step Emergency Sequence

Complete these in order. Do not skip or reorder.

```
STEP 1  Revoke the Azure AD client secret for the Graph app (ClientId: 5b20a407)
STEP 2  Revoke the Azure AD client secret for the Web app (ClientId: c83879a2)
STEP 3  Reset the SQL Server login password for 'transmittals_app'
STEP 4  Reset the Deltek ODBC credentials (UID: 52267.nucleus.prd)
STEP 5  Generate new secrets for all four of the above
STEP 6  Deploy the new secrets via environment variables (see REM-001 for method)
STEP 7  Verify the running application still works end-to-end with new secrets
STEP 8  Remove secrets from all tracked files and purge git history (see REM-001)
STEP 9  Force-push the cleaned history to all remotes
STEP 10 Notify all collaborators to re-clone (old clones still contain the secrets)
STEP 11 Audit Azure AD sign-in logs for any unexpected client-credential usage
STEP 12 Audit SQL Server login history for 'transmittals_app' over the past 90 days
```

---

## Priority Definitions

| Level | Meaning | Target Resolution |
|-------|---------|-------------------|
| **P0** | Active security risk or data-loss risk; blocks safe operation | Within 24 hours |
| **P1** | High-impact bug or security gap; degrades trust or reliability | Within 7 days |
| **P2** | Medium-impact issue; affects correctness, performance, or maintainability | Within 30 days |
| **P3** | Low-impact; cleanup, DX, or minor hardening | Backlog / next quarter |

---

## P0 — Critical / Immediate

---

### REM-001 — Rotate and Remove All Committed Credentials

| Field | Detail |
|-------|--------|
| **Source finding** | `appsettings.json` lines 3–20; `appsettings.Production.json` line 3; `_audit_publish/appsettings.json`; `_publish_audit/appsettings.json` |
| **Severity** | CRITICAL |
| **Business risk** | Full SQL Server database compromise (all booking PII); ability to send email as `reviews@korstructural.com` to any recipient; Azure AD tenant impersonation; Deltek ERP data exposure. If the repo has any remote, treat all four secrets as already stolen. |

**Short-term mitigation (do first, within hours):**
- Revoke the two Azure AD client secrets in Azure Portal → App registrations.
- Reset the SQL Server password via SSMS on KOR-APP01.
- Change the Deltek ODBC password via Deltek administration.
- Deploy temporary new secrets as OS environment variables on the app server so the running app keeps working during the cleanup.

**Long-term prevention (do after mitigation):**
- Never store secrets in any `appsettings*.json` file — tracked or untracked.
- Use .NET User Secrets for local development only (`dotnet user-secrets set`).
- Use environment variables (`ConnectionStrings__Sql`, `Graph__ClientSecret`, `AzureAd__ClientSecret`, `Deltek__OdbcDsn`) on the server or in IIS application pool settings.
- Optionally, adopt Azure Key Vault with managed identity for the production server.

**Implementation steps:**

```
1. In Azure Portal, navigate to:
     App registrations → "KOR Inspections Graph" (ClientId 5b20a407) → Certificates & secrets
     Delete the current secret; create a new one; record its value.

2. Repeat for:
     App registrations → "KOR Inspections Web" (ClientId c83879a2) → Certificates & secrets

3. On KOR-APP01, in SSMS:
     ALTER LOGIN [transmittals_app] WITH PASSWORD = '<new strong password>';

4. In Deltek administration, reset credentials for UID 52267.nucleus.prd.

5. On the production server, set environment variables before restarting the app:
     [System.Environment]::SetEnvironmentVariable("ConnectionStrings__Sql", "...", "Machine")
     [System.Environment]::SetEnvironmentVariable("Graph__ClientSecret", "...", "Machine")
     [System.Environment]::SetEnvironmentVariable("AzureAd__ClientSecret", "...", "Machine")
     [System.Environment]::SetEnvironmentVariable("Deltek__OdbcDsn", "...", "Machine")

6. Restart the app and verify booking, email, and Deltek search still work.

7. Strip secrets from all config files in the working tree:
     appsettings.json         — replace all secret values with "" (empty string)
     appsettings.Production.json — replace ClientSecret with ""
     _audit_publish/**        — delete the entire directory
     _publish_audit/**        — delete the entire directory

8. Update .gitignore (repo root) to add:
     **/appsettings.Production.json
     **/_audit_publish/
     **/_publish_audit/
     **/appsettings.Development.json
     Note: keep appsettings.json tracked but with no secrets.

9. Purge git history:
     pip install git-filter-repo        # or: brew install git-filter-repo
     git filter-repo --path Kor.Inspections.App/appsettings.json --force
     git filter-repo --path Kor.Inspections.App/appsettings.Production.json --force
     git filter-repo --path Kor.Inspections.App/_audit_publish --force
     git filter-repo --path Kor.Inspections.App/_publish_audit --force
     # Then force-push all branches:
     git push origin --force --all
     git push origin --force --tags

10. Notify every developer who has cloned the repo to delete their local clone and re-clone.
    Old clones still contain the secrets in git object storage.

11. Verify: run `git log --all --full-history -- "*/appsettings.json"` and confirm
    no commit in history contains secret values.
```

**Owner role:** Lead developer + sys-admin
**Estimated effort:** 2–4 hours
**Dependencies:** Access to Azure Portal, SSMS on KOR-APP01, Deltek admin, production server
**Rollout risk:** HIGH — brief downtime possible if environment variables are not set before app restart. Set env vars first, then rotate secrets, then redeploy. Test immediately after.

**Validation / acceptance criteria:**
- [ ] All four Azure AD client secrets are revoked and new ones issued.
- [ ] SQL Server `transmittals_app` login uses a new password not present in any file.
- [ ] Deltek ODBC credentials have been changed.
- [ ] `git log -p -- "*appsettings*" | grep -i "password\|secret\|pwd"` returns nothing.
- [ ] `_audit_publish/` and `_publish_audit/` do not appear in `git ls-files`.
- [ ] The live app can submit a booking and send email with the new secrets.
- [ ] Azure AD sign-in logs reviewed; no unexpected token issuance found.

---

### REM-002 — Restrict Admin Authorization to a Named Security Group or App Role

| Field | Detail |
|-------|--------|
| **Source finding** | `Program.cs:56` — `AuthorizeFolder("/Admin")` with no role constraint |
| **Severity** | HIGH |
| **Business risk** | Any user in the Azure AD tenant who signs in can view all client bookings (including PII), assign inspectors, send bulk emails, and cancel bookings on behalf of any client. |

**Short-term mitigation:**
- Restrict the Azure AD app registration to specific users/groups in Azure Portal:
  App registration → Enterprise application → Properties → "Assignment required: Yes" → Users and groups → add only the intended admins.
  This prevents token issuance to unauthorized users immediately, with no code change needed.

**Long-term prevention:**
- Add an App Role (`Admin`) to the app registration manifest.
- In `Program.cs`, define a named policy that requires the role claim, and apply it to the Admin folder.

**Implementation steps:**

```
Short-term (Azure Portal, no code change):
1. Azure Portal → Enterprise Applications → "KOR Inspections Web" → Properties
2. Set "Assignment required" = Yes
3. Users and groups → Add assignment → select the admin group or individual users

Long-term (code change):
1. In Azure Portal → App registrations → "KOR Inspections Web" → App roles:
   Create role: DisplayName "Admin", Value "Admin", allowed member type "Users/Groups"

2. Assign the App Role to the admin security group in Enterprise Applications.

3. In Program.cs, add after builder.Services.AddAuthorization():

   builder.Services.AddAuthorization(options =>
   {
       options.AddPolicy("AdminOnly", policy =>
           policy.RequireRole("Admin")
           // OR use a group object ID:
           // policy.RequireClaim("groups", "<group-object-id>")
       );
   });

4. Change the existing authorization convention:
   // Before:
   options.Conventions.AuthorizeFolder("/Admin");
   // After:
   options.Conventions.AuthorizeFolder("/Admin", "AdminOnly");

5. Redeploy and verify: sign in as a non-admin AD user and confirm /Admin returns 403.
   Sign in as an admin user and confirm /Admin loads correctly.
```

**Owner role:** Lead developer + Azure AD administrator
**Estimated effort:** 1–2 hours (short-term 15 min; long-term 1–2 hours including testing)
**Dependencies:** REM-001 must be done first (new AzureAd client secret must be in place)
**Rollout risk:** LOW — existing admin users continue to work if the App Role is assigned before deploying

**Validation / acceptance criteria:**
- [ ] Non-admin AD user receives 403 on `/Admin`.
- [ ] Admin AD user (with role assigned) can access `/Admin` normally.
- [ ] `Program.cs` contains `RequireRole("Admin")` or equivalent claim check.
- [ ] Azure AD app registration "Assignment required" is set to Yes.

---

## P1 — High / Next 7 Days

---

### REM-003 — Fix GraphMailService Token Caching (MSAL Singleton)

| Field | Detail |
|-------|--------|
| **Source finding** | `Services/GraphMailService.cs:37-46` — new `ConfidentialClientApplication` per call |
| **Severity** | HIGH (performance) |
| **Business risk** | Every email (booking confirmation, cancellation, assignment, summary) makes a fresh Azure AD client-credentials flow. Under moderate load (e.g., batch summary sends), this adds 100–400ms latency per email and risks Azure AD throttling. |

**Short-term mitigation:** None required; the current code is correct but slow.

**Long-term prevention:**

**Implementation steps:**

```
1. Change GraphMailService to hold a singleton IConfidentialClientApplication.
   Replace the local variable in GetTokenAsync() with a lazily-initialized field:

   private IConfidentialClientApplication? _msalApp;
   private readonly SemaphoreSlim _msalLock = new(1, 1);

   private async Task<IConfidentialClientApplication> GetOrCreateAppAsync()
   {
       if (_msalApp != null) return _msalApp;
       await _msalLock.WaitAsync();
       try
       {
           if (_msalApp != null) return _msalApp;
           var tenantId = _config["Graph:TenantId"] ?? throw new InvalidOperationException("Missing Graph:TenantId");
           var clientId = _config["Graph:ClientId"] ?? throw new InvalidOperationException("Missing Graph:ClientId");
           var secret   = _config["Graph:ClientSecret"] ?? throw new InvalidOperationException("Missing Graph:ClientSecret");
           _msalApp = ConfidentialClientApplicationBuilder
               .Create(clientId)
               .WithClientSecret(secret)
               .WithAuthority($"https://login.microsoftonline.com/{tenantId}")
               .Build();
           return _msalApp;
       }
       finally { _msalLock.Release(); }
   }

2. Register GraphMailService as Singleton (not Scoped) in Program.cs:
   // Before:
   builder.Services.AddScoped<GraphMailService>();
   // After:
   builder.Services.AddSingleton<GraphMailService>();

   Note: GraphMailService currently injects IConfiguration (singleton-safe) and
   IHttpClientFactory (singleton-safe). Verify no scoped dependency is added.

3. Remove the per-call ConfidentialClientApplicationBuilder pattern from GetTokenAsync().

4. Test: send two emails back-to-back; confirm only one AAD token request is visible
   in Azure AD sign-in logs (subsequent calls should use the cached token).
```

**Owner role:** Lead developer
**Estimated effort:** 2 hours (including testing)
**Dependencies:** None
**Rollout risk:** LOW — behavior is identical; only token caching changes

**Validation / acceptance criteria:**
- [ ] Azure AD sign-in logs show one token issuance per token lifetime (~1 hour), not one per email.
- [ ] Emails still send correctly after the change.
- [ ] `GraphMailService` is registered as `Singleton` in `Program.cs`.

---

### REM-004 — Fix `PhoneNormalizer.Format(null)` NullReferenceException

| Field | Detail |
|-------|--------|
| **Source finding** | `Services/PhoneNormalizer.cs:19-24` |
| **Severity** | MEDIUM (confirmed latent bug) |
| **Business risk** | `Normalize()` returns `null` when input is `null`; `Format()` then calls `.Length` on that null, throwing `NullReferenceException`. Currently guarded by non-nullable model properties, but any future code path passing null would crash. |

**Implementation steps:**

```
In Services/PhoneNormalizer.cs, change Normalize():

// Before:
if (string.IsNullOrWhiteSpace(phone))
    return phone;

// After:
if (string.IsNullOrWhiteSpace(phone))
    return string.Empty;

No other changes needed; Format() already handles empty string gracefully.
```

**Owner role:** Developer
**Estimated effort:** 15 minutes
**Dependencies:** None
**Rollout risk:** NONE — behaviour is identical for all non-null inputs; null now returns `""` instead of throwing

**Validation / acceptance criteria:**
- [ ] `PhoneNormalizer.Format(null)` returns `""` without throwing.
- [ ] `PhoneNormalizer.Format("")` returns `""` without throwing.
- [ ] Existing phone formatting tests (add if none exist) still pass.

---

### REM-005 — Extract `IsCancellationAllowed` from Two Page Models into `TimeRuleService`

| Field | Detail |
|-------|--------|
| **Source finding** | `Pages/Manage.cshtml.cs:125-150` and `Pages/Index.cshtml.cs:870-895` — identical method duplicated |
| **Severity** | MEDIUM (maintainability / correctness risk) |
| **Business risk** | If the cancellation policy changes (e.g., cutoff time moves, holidays are added), one copy will inevitably be missed, causing inconsistent enforcement between the Manage page and the inline cancel endpoint. |

**Implementation steps:**

```
1. Add to TimeRuleService:

   /// <summary>
   /// Cancellation is allowed until 14:00 local time on the previous business day.
   /// </summary>
   public bool IsCancellationAllowed(DateTime bookingStartUtc)
   {
       var nowLocal     = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, _tz);
       var bookingLocal = TimeZoneInfo.ConvertTimeFromUtc(bookingStartUtc, _tz);

       if (bookingLocal <= nowLocal)
           return false;

       var cutoffDay = bookingLocal.Date.AddDays(-1);
       while (cutoffDay.DayOfWeek == DayOfWeek.Saturday ||
              cutoffDay.DayOfWeek == DayOfWeek.Sunday)
           cutoffDay = cutoffDay.AddDays(-1);

       var cutoffLocal = new DateTime(
           cutoffDay.Year, cutoffDay.Month, cutoffDay.Day,
           _options.CutoffHourLocal, 0, 0,
           DateTimeKind.Unspecified);

       return nowLocal <= cutoffLocal;
   }

   Note: use _options.CutoffHourLocal (already in InspectionRulesOptions) instead of
   the hardcoded 14 in the current duplicates.

2. In ManageModel: replace the private IsCancellationAllowed() with a call to
   _timeRules.IsCancellationAllowed(booking.StartUtc).

3. In IndexModel: same replacement.

4. Delete both private IsCancellationAllowed() methods.

5. Regression test: verify cancellation is blocked after the cutoff and allowed before it.
```

**Owner role:** Developer
**Estimated effort:** 30 minutes
**Dependencies:** None
**Rollout risk:** NONE — logic is identical; the cutoff hour now comes from config instead of a magic number

**Validation / acceptance criteria:**
- [ ] No `private bool IsCancellationAllowed` exists in `ManageModel` or `IndexModel`.
- [ ] `TimeRuleService.IsCancellationAllowed` exists and is called by both page models.
- [ ] Manual test: booking for today cannot be cancelled; booking two days out can be cancelled before 14:00 the prior business day.

---

### REM-006 — Remove Dead `GetBrowserScope()` Method

| Field | Detail |
|-------|--------|
| **Source finding** | `Services/ProjectBootstrapVerificationService.cs:262-276` |
| **Severity** | MEDIUM (misleading dead code) |
| **Business risk** | `GetBrowserScope()` generates and stores a per-session GUID, implying the verification cache is session-scoped. It is never called. Future developers may assume session isolation exists when it does not, leading to security mistakes. |

**Implementation steps:**

```
Option A — Remove (recommended if session-scope was never intended):
  Delete lines 262–276 (the GetBrowserScope() method) from
  ProjectBootstrapVerificationService.cs.
  Remove the private readonly IHttpContextAccessor _httpContextAccessor field
  and its constructor parameter if no other method uses it.

Option B — Implement (if session-scope was intended):
  Wire GetBrowserScope() into BuildKey():
  return $"proj-bootstrap:{projectNumber}|{domain}|{GetBrowserScope()}";
  This would make verification session-specific rather than domain-wide.
  Requires re-evaluation of the domain-trust-persistence logic.
```

**Owner role:** Developer
**Estimated effort:** 20 minutes
**Dependencies:** Decision on whether session-scoping was intended (discuss with product owner)
**Rollout risk:** NONE for Option A; LOW for Option B

**Validation / acceptance criteria:**
- [ ] Either `GetBrowserScope()` is deleted and `IHttpContextAccessor` is removed from the constructor, OR it is actively called in `BuildKey()` with a recorded design decision.
- [ ] No compiler warnings about unused private methods.

---

### REM-007 — Replace Hardcoded From-Mailbox String Literal

| Field | Detail |
|-------|--------|
| **Source finding** | `Services/BookingService.cs:163, 228, 293`; `Pages/Admin/Summary.cshtml.cs:140` |
| **Severity** | MEDIUM (maintainability) |
| **Business risk** | The string `"reviews@korstructural.com"` appears four times as the Graph `sendMail` from-address. If the mailbox changes, it must be updated in all four locations. One missed update would cause email sends to fail with a Graph 404. |

**Implementation steps:**

```
1. Add a property to NotificationOptions:
   public string FromMailbox { get; set; } = string.Empty;

2. Add to appsettings.json under "Notification":
   "FromMailbox": "reviews@korstructural.com"

3. In BookingService: inject IOptions<NotificationOptions> (already done).
   Replace all three occurrences of:
     var fromMailbox = "reviews@korstructural.com";
   with:
     var fromMailbox = _notificationOptions.FromMailbox;

4. In SummaryModel: inject IOptions<NotificationOptions> (add to constructor).
   Replace:
     var fromMailbox = "reviews@korstructural.com";
   with:
     var fromMailbox = _notificationOptions.FromMailbox;

5. Add validation in ValidateInspectionRulesConfiguration (or a new
   ValidateNotificationConfiguration) to throw on startup if FromMailbox is empty
   in production.
```

**Owner role:** Developer
**Estimated effort:** 45 minutes
**Dependencies:** None
**Rollout risk:** NONE — functional behaviour is identical

**Validation / acceptance criteria:**
- [ ] Zero occurrences of `"reviews@korstructural.com"` as a string literal in any `.cs` file.
- [ ] `NotificationOptions.FromMailbox` is set in config and used in all email sends.
- [ ] Startup validation fails if `FromMailbox` is empty in Production.

---

## P2 — Medium / Next 30 Days

---

### REM-008 — Implement the `BookingActions` Audit Trail

| Field | Detail |
|-------|--------|
| **Source finding** | `Data/BookingAction.cs` and `Data/InspectionsContext.cs` — table exists, never written |
| **Severity** | MEDIUM (incomplete feature; missing audit capability) |
| **Business risk** | No audit record of who assigned, cancelled, or created any booking. Disputes about whether a booking was placed or cancelled cannot be investigated. In a professional inspection context, this is a compliance gap. |

**Implementation steps:**

```
1. Create a helper or extension method to write actions:

   // In BookingService or a new BookingAuditService:
   private async Task RecordActionAsync(
       Guid bookingId, string actionType, string? performedBy, string? notes = null)
   {
       _db.BookingActions.Add(new BookingAction
       {
           BookingId   = bookingId,
           ActionType  = actionType,
           PerformedBy = performedBy,
           Notes       = notes,
           ActionUtc   = DateTime.UtcNow
       });
       // Do NOT call SaveChangesAsync here; let the caller's SaveChanges include it.
   }

2. In CreateBookingAsync: call RecordActionAsync(booking.BookingId, "Created",
   booking.ContactEmail) before SaveChangesAsync().

3. In CancelBookingByTokenAsync: call RecordActionAsync(booking.BookingId, "Cancelled",
   "client-token") before SaveChangesAsync().

4. In Admin/Index.OnPostAssignAsync: call RecordActionAsync(booking.BookingId, "Assigned",
   User.Identity?.Name, $"Assigned to {booking.AssignedTo}") before SaveChangesAsync().

5. In Admin/Index.OnPostCancelAsync: call RecordActionAsync(booking.BookingId, "Cancelled",
   User.Identity?.Name) before SaveChangesAsync().

6. In Index.OnPostCancelInspectionAsync: call RecordActionAsync(booking.BookingId,
   "Cancelled", emailRaw) before SaveChangesAsync().

7. Add a migration: dotnet ef migrations add AddBookingActionsAuditWrites
   (no schema change needed; table already exists)

8. Optionally add a read view in the Admin booking detail page.
```

**Owner role:** Developer
**Estimated effort:** 3–4 hours
**Dependencies:** None
**Rollout risk:** LOW — additive only; no schema change

**Validation / acceptance criteria:**
- [ ] Creating a booking inserts one `BookingAction` row with type `"Created"`.
- [ ] Cancelling a booking (any path) inserts one `BookingAction` row with type `"Cancelled"`.
- [ ] Assigning a booking inserts one `BookingAction` row with type `"Assigned"`.
- [ ] `BookingActions` table grows proportionally with booking activity.

---

### REM-009 — Fix EF Core LINQ Queries Using `.ToLower()` / `.Trim()` in Predicates

| Field | Detail |
|-------|--------|
| **Source finding** | `Pages/Index.cshtml.cs:362-365`; `Services/ProjectBootstrapVerificationService.cs:65-68, 202-204` |
| **Severity** | MEDIUM (performance) |
| **Business risk** | `LOWER(ContactEmail)` and `LTRIM(RTRIM(EmailDomain))` in SQL predicates prevent index seeks, causing full scans as the `Bookings` and `ProjectDefaults` tables grow. |

**Implementation steps:**

```
Fix 1 — OnPostLookupInspectionsAsync and OnPostCancelInspectionAsync (Index.cshtml.cs):
  The domain suffix check currently does:
    b.ContactEmail.ToLower().EndsWith(domainSuffix)
  Replace with:
    EF.Functions.Like(b.ContactEmail, "%" + domainSuffix)
  domainSuffix is already lowercase (derived from the normalized email).
  Ensure the database column collation is case-insensitive (SQL Server default is CI).

Fix 2 — ProjectBootstrapVerificationService GetStatusAsync / VerifyCodeAsync:
  The ProjectDefaults query currently does:
    x.EmailDomain.Trim().ToLower() == domain
  Change to:
    x.EmailDomain == domain
  This works because:
    a) domain is already normalized to lowercase in NormalizeEmail()
    b) EmailDomain is stored via VerifyCodeAsync which already lowercases it
    c) Removing Trim() is safe if we ensure data is stored pre-trimmed (it is)

Fix 3 — Add an index on Bookings.ContactEmail if not already present:
  In InspectionsContext.OnModelCreating, add:
    entity.HasIndex(b => b.ContactEmail);
```

**Owner role:** Developer
**Estimated effort:** 1–2 hours
**Dependencies:** Verify database collation is case-insensitive (likely SQL_Latin1_General_CP1_CI_AS)
**Rollout risk:** LOW — behaviour is identical given CI collation and pre-normalized data; add migration for the index

**Validation / acceptance criteria:**
- [ ] No `.ToLower()` or `.Trim()` calls inside EF `Where()` lambdas.
- [ ] `LOWER(` does not appear in EF Core query logs for the affected endpoints.
- [ ] An index on `Bookings.ContactEmail` exists in the database.

---

### REM-010 — Validate `assignedTo` Against Inspector List in Admin Assign

| Field | Detail |
|-------|--------|
| **Source finding** | `Pages/Admin/Index.cshtml.cs:123-168` — `OnPostAssignAsync` |
| **Severity** | MEDIUM (data integrity / silent failure) |
| **Business risk** | An admin can assign a booking to a misspelled or arbitrary name. No error is shown; the assignment email fires but the inspector lookup (`i.DisplayName == booking.AssignedTo`) returns null, so the inspector never receives notification. |

**Implementation steps:**

```
In OnPostAssignAsync, after the existing null/cancel/complete guard:

if (!string.IsNullOrWhiteSpace(assignedTo) &&
    !assignedTo.Equals("Unassigned", StringComparison.OrdinalIgnoreCase))
{
    var inspectorExists = await _db.Inspectors
        .AnyAsync(i => i.Enabled && i.DisplayName == assignedTo.Trim());

    if (!inspectorExists)
    {
        StatusMessage = $"Inspector '{assignedTo}' not found or is disabled.";
        return RedirectToPage(...);
    }
}
```

**Owner role:** Developer
**Estimated effort:** 30 minutes
**Dependencies:** None
**Rollout risk:** NONE — only blocks currently-silent failures

**Validation / acceptance criteria:**
- [ ] Assigning to a nonexistent inspector name returns an error message.
- [ ] Assigning to a valid inspector name succeeds as before.

---

### REM-011 — Add Rate Limiting to Contact Save / Delete Endpoints

| Field | Detail |
|-------|--------|
| **Source finding** | `Pages/Index.cshtml.cs` — `OnPostSaveContactAjaxAsync`, `OnPostDeleteContactAsync`, `OnPostSelectContactAsync` |
| **Severity** | MEDIUM (security/availability) |
| **Business risk** | An authenticated or OTP-verified attacker could flood the contact table with entries or repeatedly hammer the delete endpoint. No rate limiting is applied to these mutation endpoints. |

**Implementation steps:**

```
1. Define a new rate-limit policy in Program.cs:

   options.AddPolicy("contactMutation", httpContext =>
       RateLimitPartition.GetFixedWindowLimiter(
           partitionKey: GetRateLimitPartitionKey(httpContext),
           factory: _ => new FixedWindowRateLimiterOptions
           {
               PermitLimit = 20,
               Window = TimeSpan.FromMinutes(10),
               QueueLimit = 0
           }));

2. Decorate each affected handler:
   [EnableRateLimiting("contactMutation")]
   public async Task<JsonResult> OnPostSaveContactAjaxAsync(...)

   [EnableRateLimiting("contactMutation")]
   public async Task<IActionResult> OnPostDeleteContactAsync(int id)

   [EnableRateLimiting("contactMutation")]
   public async Task<JsonResult> OnPostSelectContactAsync(int id)
```

**Owner role:** Developer
**Estimated effort:** 30 minutes
**Dependencies:** None
**Rollout risk:** NONE — only affects abnormal usage patterns

**Validation / acceptance criteria:**
- [ ] Sending 21+ contact save requests within 10 minutes from the same IP returns HTTP 429.
- [ ] Normal usage (< 20 ops per 10 min) is unaffected.

---

### REM-012 — Move `PublicBaseUrl` Localhost Default to Development Config

| Field | Detail |
|-------|--------|
| **Source finding** | `appsettings.json:App:PublicBaseUrl = "https://localhost:7074"` |
| **Severity** | LOW (configuration drift) |
| **Business risk** | If a staging or test environment is deployed without a Production config overlay, cancellation and confirmation URLs in all outbound emails will point to `https://localhost:7074`, which is unreachable. |

**Implementation steps:**

```
1. In appsettings.json, change:
   "App": { "PublicBaseUrl": "https://localhost:7074" }
   to:
   "App": { "PublicBaseUrl": "" }

2. In appsettings.Development.json, add:
   "App": { "PublicBaseUrl": "https://localhost:7074" }

3. Confirm: in all non-Development environments, PublicBaseUrl must be set via
   environment variable or config overlay.

4. Add PublicBaseUrl to the ValidateRequiredSecret checks in Program.cs for Production.
```

**Owner role:** Developer
**Estimated effort:** 15 minutes
**Dependencies:** None
**Rollout risk:** NONE — development behavior unchanged; production already has the correct URL in the Production config (now removed from source)

**Validation / acceptance criteria:**
- [ ] `appsettings.json` has `"PublicBaseUrl": ""`.
- [ ] `appsettings.Development.json` has `"PublicBaseUrl": "https://localhost:7074"`.
- [ ] Booking confirmation email in production contains the correct public URL.

---

### REM-013 — Document and Test Domain-Scoped Verification Design

| Field | Detail |
|-------|--------|
| **Source finding** | `Services/ProjectBootstrapVerificationService.cs:278-286` — cache key is domain-scoped, not user-scoped |
| **Severity** | MEDIUM (reliability + implicit design assumption) |
| **Business risk** | Two users from the same email domain requesting a code simultaneously will overwrite each other's pending code in the cache. One user's verification silently fails. This is a UX defect and creates confusion for clients trying to verify concurrently. |

**Short-term mitigation:**
- Add a comment to `BuildKey()` explicitly documenting that the scope is intentionally domain-level, and note the concurrency limitation.

**Long-term fix:**

```
Option A — Per-email cache key (breaks current domain-trust design):
  Change BuildKey() to use the full normalized email instead of domain.
  This means each user verifies independently; domain trust is still persisted
  to ProjectDefaults on first verification from any address in the domain.

Option B — Keep domain-scope but detect concurrent code requests:
  In SendCodeAsync(), before overwriting a pending state, check if one is
  already pending and unexpired. If so, re-send the same code rather than
  generating a new one. This prevents invalidating a concurrent user's code.

Recommended: Option B — lower impact, better UX.

  if (_cache.TryGetValue<VerificationState>(key, out var existing)
      && existing is { Verified: false }
      && existing.FailedAttempts == 0)
  {
      // Resend the same code — do not overwrite
      // ... send email with existing.Code
      return true;
  }
  // else generate new code as before
```

**Owner role:** Developer + product owner
**Estimated effort:** 1–2 hours
**Dependencies:** Product decision on whether domain-scope is correct
**Rollout risk:** LOW

**Validation / acceptance criteria:**
- [ ] `BuildKey()` has an XML doc comment explaining domain-scope design.
- [ ] Two concurrent `SendCodeAsync` calls from the same domain do not invalidate each other.

---

### REM-014 — Add `MaxBookingsPerSlot` to `appsettings.json`

| Field | Detail |
|-------|--------|
| **Source finding** | `Options/InspectionRulesOptions.cs` — `MaxBookingsPerSlot` defaults to 3 but is absent from config |
| **Severity** | LOW (operational visibility) |
| **Business risk** | Operators reading the config file cannot see the slot capacity setting. An accidental removal of a production config override would silently revert to 3. |

**Implementation steps:**

```
Add to appsettings.json under "InspectionRules":
  "MaxBookingsPerSlot": 3
```

**Owner role:** Developer
**Estimated effort:** 5 minutes
**Dependencies:** None
**Rollout risk:** NONE

**Validation / acceptance criteria:**
- [ ] `"MaxBookingsPerSlot"` key exists in `appsettings.json`.

---

## P3 — Low / Backlog

---

### REM-015 — Add Unit Tests for Core Business Logic

| Field | Detail |
|-------|--------|
| **Source finding** | No `*.Tests.csproj`; only one Playwright e2e test exists |
| **Severity** | HIGH (testing gap) |
| **Business risk** | Regressions in slot calculation, overlap detection, cancellation window, PIN validation, and booking creation go undetected until they reach production. |

**Implementation steps:**

```
1. Create Kor.Inspections.Tests/Kor.Inspections.Tests.csproj (xUnit + Moq).

2. Priority test cases:
   a. TimeRuleService.GetAvailableSlotsForDate
      - All slots open when no bookings
      - Slot blocked when booking overlaps (including padding)
      - Slots at start/end of work day
      - Slots on weekend (should be excluded from booking window)

   b. TimeRuleService.GetAllowedDateRangeUtcNow
      - Before cutoff hour: minDate = tomorrow
      - After cutoff hour: minDate = day after tomorrow

   c. TimeRuleService.IsCancellationAllowed (after REM-005)
      - Past booking: not allowed
      - Monday booking, checked Friday 13:59: allowed
      - Monday booking, checked Friday 14:01: not allowed
      - Tuesday booking, checked Monday 13:59: allowed

   d. ProjectAccessService.ValidatePinAsync
      - Correct PIN returns true
      - Wrong PIN returns false
      - Disabled project returns false
      - Timing attack: constant-time comparison verified

   e. PhoneNormalizer
      - Null input: returns ""
      - 10-digit number: formats correctly
      - 11-digit with leading 1: strips prefix and formats
      - Non-numeric input: returns original

   f. ProjectNumberHelper.Base5
      - "30844-01" returns "30844"
      - "308" returns ""
      - null returns ""
```

**Owner role:** Developer
**Estimated effort:** 1–2 days
**Dependencies:** REM-005 (extract `IsCancellationAllowed` first)
**Rollout risk:** NONE

**Validation / acceptance criteria:**
- [ ] `dotnet test` runs and passes with ≥ 80% coverage of `TimeRuleService`, `ProjectAccessService`, and `PhoneNormalizer`.
- [ ] CI pipeline (when set up) runs `dotnet test` on every push.

---

### REM-016 — Fix E2E Test Auth and Environment Setup

| Field | Detail |
|-------|--------|
| **Source finding** | `tests/e2e/playwright.config.ts` and `admin-mobile-inspector.spec.ts` |
| **Severity** | MEDIUM (DX / CI) |
| **Business risk** | The single e2e test navigates directly to `/admin` with no auth setup. The admin page requires Azure AD authentication. The test can only pass against a development environment with auth disabled or with a pre-authenticated session, which is undocumented. |

**Implementation steps:**

```
1. Add a Playwright storageState setup fixture that signs in to the Azure AD
   test application using a dedicated test account.

2. Document required environment variables:
   BASE_URL, TEST_ADMIN_EMAIL, TEST_ADMIN_PASSWORD (or use client-credentials for testing)

3. Add a playwright.config.ts globalSetup step:
   globalSetup: './setup/auth.ts'

4. Ensure the test account has the Admin app role (after REM-002).

5. Add to README or a TESTING.md: how to run e2e tests locally.
```

**Owner role:** Developer
**Estimated effort:** 3–4 hours
**Dependencies:** REM-002 (admin role enforcement must be in place)
**Rollout risk:** NONE

**Validation / acceptance criteria:**
- [ ] `npx playwright test` passes in a CI environment with correct env vars set.
- [ ] Test does not require a manually pre-authenticated browser session.

---

### REM-017 — Add Admin Grid Pagination / Max Date Range Cap

| Field | Detail |
|-------|--------|
| **Source finding** | `Pages/Admin/Index.cshtml.cs:229` — no `.Take()` on booking query |
| **Severity** | LOW (scalability) |
| **Business risk** | An admin selecting a date range spanning months could pull thousands of rows into memory and render them in a single HTML page. Acceptable for current scale; a risk at higher volume. |

**Implementation steps:**

```
1. Add a max window cap (e.g., 90 days) in LoadDataAsync():
   if ((windowEndLocal - windowStartLocal).TotalDays > 90)
   {
       windowEndLocal = windowStartLocal.AddDays(90);
       // Optionally add a warning to StatusMessage
   }

2. Alternatively, add server-side pagination with page number and page size parameters.
```

**Owner role:** Developer
**Estimated effort:** 1–2 hours
**Dependencies:** None
**Rollout risk:** LOW

**Validation / acceptance criteria:**
- [ ] Date range > 90 days is capped or returns a user-visible warning.

---

### REM-018 — Use Email-or-ID Rather Than DisplayName to Identify Inspectors

| Field | Detail |
|-------|--------|
| **Source finding** | `Services/BookingService.cs:192` — `i.DisplayName == booking.AssignedTo` |
| **Severity** | MEDIUM (data integrity, long-term) |
| **Business risk** | Inspector assignment and email notification both rely on a display name string match. Renaming an inspector in the `Inspectors` table orphans all existing `Booking.AssignedTo` values and silently stops email delivery. |

**Implementation steps:**

```
This is a larger refactor. Sequence:

Phase 1 (safe, immediate):
  - Apply REM-010 to validate assignedTo against the inspector list.
  - This prevents new orphan assignments.

Phase 2 (schema change):
  - Add Inspector.Email to Booking as a nullable AssignedToEmail column.
  - On assign, store both AssignedTo (display, for display only) and AssignedToEmail.
  - Migration: dotnet ef migrations add AddAssignedToEmail

Phase 3 (lookup fix):
  - In BookingService, look up inspector by email rather than display name.
  - In email sending, use AssignedToEmail directly rather than querying the Inspector table.

Phase 4 (cleanup):
  - Consider deprecating AssignedTo string in favour of InspectorId FK in a future migration.
```

**Owner role:** Developer
**Estimated effort:** Phase 1: 30 min; Phase 2–4: 3–4 hours
**Dependencies:** REM-010 should be done first
**Rollout risk:** LOW for Phase 1; MEDIUM for Phase 2–3 (requires migration and data backfill)

**Validation / acceptance criteria:**
- [ ] Renaming an inspector's display name does not prevent them from receiving notification emails.
- [ ] Existing assigned bookings retain a correct inspector reference after renaming.

---

### REM-019 — Add `.gitignore` Entries for Build Artifacts

| Field | Detail |
|-------|--------|
| **Source finding** | `_audit_publish/`, `_publish_audit/` directories committed to git (also covered in REM-001) |
| **Severity** | LOW (DX / hygiene) |
| **Business risk** | Future publish runs will re-create these directories and developers may commit them again, re-introducing credential exposure. |

**Implementation steps:**

```
Add to the repo-root .gitignore (and the app-level .gitignore):

# Publish output directories — never commit
**/publish/
**/_publish*/
**/_audit_publish*/
**/Properties/PublishProfiles/

Also verify the existing .gitignore covers:
  appsettings.Production.json
  appsettings.*.json (except Development and base)
  *.user
  .vs/
  bin/
  obj/
```

**Owner role:** Developer
**Estimated effort:** 15 minutes
**Dependencies:** REM-001 (already removes the directories)
**Rollout risk:** NONE

**Validation / acceptance criteria:**
- [ ] `git status` does not show `_audit_publish/` or `_publish_audit/` as untracked after a `dotnet publish` run.
- [ ] `git ls-files | grep _audit` returns nothing.

---

## Section: Immediate Containment Actions

These are actions that reduce risk **right now** without code changes:

| # | Action | Time required | Who |
|---|--------|---------------|-----|
| 1 | Revoke both Azure AD client secrets in Azure Portal | 5 min | Azure admin |
| 2 | Reset SQL Server `transmittals_app` password | 5 min | DBA |
| 3 | Reset Deltek ODBC credentials | 5 min | DBA / Deltek admin |
| 4 | Set new credentials as environment variables on the production server | 15 min | Sys-admin |
| 5 | Restart the app process and verify it starts cleanly | 5 min | Sys-admin |
| 6 | In Azure Portal: set "Assignment required = Yes" on the Web app Enterprise Application; add only the intended admins | 15 min | Azure admin |
| 7 | Review Azure AD sign-in logs for `ClientId 5b20a407` and `c83879a2` for unexpected token issuance over the past 90 days | 30 min | Azure admin |
| 8 | Review SQL Server login history for `transmittals_app` | 20 min | DBA |

---

## Section: Permanent Fixes

These require code changes and deployment:

| Remediation | What changes | Type |
|-------------|-------------|------|
| REM-001 | Remove secrets from config files and git history | Security |
| REM-002 | Add Admin policy with role/group requirement | Security |
| REM-003 | MSAL singleton + GraphMailService registered as singleton | Performance |
| REM-004 | `PhoneNormalizer` null guard | Bug fix |
| REM-005 | Extract `IsCancellationAllowed` to `TimeRuleService` | Refactor |
| REM-007 | From-mailbox from config | Maintainability |
| REM-008 | Write `BookingAction` records on all lifecycle events | Feature completion |
| REM-009 | Remove `.ToLower()` from EF predicates; add index | Performance |
| REM-010 | Validate `assignedTo` against inspector list | Data integrity |
| REM-011 | Rate-limit contact mutation endpoints | Security |
| REM-012 | Move localhost URL to Development config | Config hygiene |
| REM-018 | Store `AssignedToEmail` on booking (phased) | Architecture |

---

## Section: Cleanup and Follow-up Work

| Remediation | What it cleans up |
|-------------|-------------------|
| REM-006 | Remove dead `GetBrowserScope()` method |
| REM-013 | Document and test domain-scoped verification design |
| REM-014 | Add `MaxBookingsPerSlot` to visible config |
| REM-015 | Unit test suite for core services |
| REM-016 | Fix e2e test auth setup |
| REM-017 | Admin grid pagination / date range cap |
| REM-019 | `.gitignore` entries for publish artifacts |

---

## Timeline Summary

### First 24 Hours (Emergency)

```
[ ] Revoke Azure AD client secret for Graph app (5b20a407)
[ ] Revoke Azure AD client secret for Web app (c83879a2)
[ ] Reset SQL Server transmittals_app password
[ ] Reset Deltek ODBC credentials
[ ] Deploy new credentials as environment variables on production server
[ ] Verify app runs with new credentials
[ ] Strip secrets from appsettings.json and appsettings.Production.json
[ ] Add _audit_publish/, _publish_audit/ to .gitignore
[ ] Run git filter-repo to purge secrets from git history
[ ] Force-push cleaned history to all remotes
[ ] Notify all collaborators to re-clone
[ ] Set "Assignment required = Yes" in Azure AD (stops unauthorized admin access immediately)
[ ] Review Azure AD and SQL sign-in logs for unexpected access
```

### Next 7 Days

```
[ ] REM-002 — Code change: add AdminOnly policy and role check
[ ] REM-003 — MSAL singleton in GraphMailService
[ ] REM-004 — PhoneNormalizer null guard
[ ] REM-005 — Extract IsCancellationAllowed to TimeRuleService
[ ] REM-006 — Remove dead GetBrowserScope()
[ ] REM-007 — From-mailbox from config
[ ] REM-012 — Move PublicBaseUrl to Development config
[ ] REM-014 — Add MaxBookingsPerSlot to appsettings.json
[ ] REM-019 — Update .gitignore
```

### Next 30 Days

```
[ ] REM-008 — Implement BookingAction audit writes
[ ] REM-009 — Fix EF LINQ predicate performance + add index
[ ] REM-010 — Validate assignedTo against inspector list
[ ] REM-011 — Rate-limit contact mutation endpoints
[ ] REM-013 — Resolve domain-scope verification design
[ ] REM-015 — Unit test suite (begin with TimeRuleService and PhoneNormalizer)
[ ] REM-016 — Fix e2e test auth setup
[ ] REM-017 — Admin grid date range cap
[ ] REM-018 Phase 1 — Validate assignedTo (prerequisite for Phase 2)
```

---

## Finding-to-Remediation Mapping Table

| Finding ID | Finding Summary | Remediation | Priority | Status |
|------------|-----------------|-------------|----------|--------|
| F-01 | Live credentials in `appsettings.json` (SQL, Graph, AzureAd, Deltek) | REM-001 | P0 | ☐ Open |
| F-02 | Production AzureAd secret in `appsettings.Production.json` | REM-001 | P0 | ☐ Open |
| F-03 | Credential copies in `_audit_publish/` directories | REM-001, REM-019 | P0 | ☐ Open |
| F-04 | Admin `/Admin` accepts any AD user (no role check) | REM-002 | P0 | ☐ Open |
| F-05 | MSAL `ConfidentialClientApplication` created per email send | REM-003 | P1 | ☐ Open |
| F-06 | `PhoneNormalizer.Format(null)` throws NullReferenceException | REM-004 | P1 | ☐ Open |
| F-07 | `IsCancellationAllowed` duplicated in `ManageModel` and `IndexModel` | REM-005 | P1 | ☐ Open |
| F-08 | `GetBrowserScope()` is dead code, never called | REM-006 | P1 | ☐ Open |
| F-09 | From-mailbox hardcoded as string literal in 4 places | REM-007 | P1 | ☐ Open |
| F-10 | `BookingActions` table exists but is never written to | REM-008 | P2 | ☐ Open |
| F-11 | EF predicates use `.ToLower()` / `.Trim()` — prevents index use | REM-009 | P2 | ☐ Open |
| F-12 | `assignedTo` not validated against inspector list | REM-010 | P2 | ☐ Open |
| F-13 | Contact mutation endpoints have no rate limiting | REM-011 | P2 | ☐ Open |
| F-14 | `PublicBaseUrl` localhost value in base config | REM-012 | P2 | ☐ Open |
| F-15 | Domain-scoped verification cache key causes concurrent-user code collision | REM-013 | P2 | ☐ Open |
| F-16 | `MaxBookingsPerSlot` not visible in config | REM-014 | P2 | ☐ Open |
| F-17 | Inspector assignment uses display name string (no FK) | REM-010, REM-018 | P2/P3 | ☐ Open |
| F-18 | No unit tests for any business logic | REM-015 | P3 | ☐ Open |
| F-19 | E2E test has no auth setup; fails in CI | REM-016 | P3 | ☐ Open |
| F-20 | Admin grid has no result limit for wide date ranges | REM-017 | P3 | ☐ Open |
| F-21 | `BookingDisplayHelper` has redundant double `if` | — (trivial cleanup) | P3 | ☐ Open |
| F-22 | `PhoneNormalizer` declared in global namespace (no namespace) | — (style fix) | P3 | ☐ Open |
| F-23 | Email sending failure after commit — no retry mechanism | REM-008 (partial) | P2 | ☐ Open |
| F-24 | Verification email bombing via unauthenticated OTP endpoint | REM-011 (partial) | P2 | ☐ Open |
| F-25 | `GetFullyBookedDatesAsync` O(n×m) in-memory loop | — (acceptable at scale) | P3 | ☐ Open |
| F-26 | `appsettings.Production.json` in git without .gitignore entry | REM-001, REM-019 | P0 | ☐ Open |

---

*Last updated: 2026-03-09. Update status column as items are resolved.*
