# Remediation Report

Date: 2026-03-13

## Project Summary

KOR Inspections Bookings is a .NET 8 Razor Pages application for booking, managing, and administering field reviews, with SQL Server persistence, Deltek ODBC integration, Microsoft Entra ID admin authentication, email delivery through Microsoft Graph, and a separate Playwright E2E test suite.

## Severity Summary

| Severity | Count | Summary |
| --- | ---: | --- |
| Critical | 2 | Immediate exposure of committed secrets and an unauthenticated path to retrieve project inspection data. |
| High | 2 | Authorization is overly broad because trust and cancellation are granted at the email-domain level rather than the verified user level. |
| Medium | 6 | Public observability leakage, scaling/performance weaknesses, missing indexes, inconsistent error handling, oversized page architecture, and missing automated coverage for sensitive flows. |
| Low | 3 | Dead legacy code, non-reproducible dependency sourcing, and deployment settings that can leave stale files in production. |

## 1. Hardcoded Production Secrets And Credentials Committed To Source

- Severity: Critical
- File paths:
  - `Kor.Inspections.App/appsettings.json`
  - `Kor.Inspections.App/appsettings.Production.json`
- Line numbers:
  - `appsettings.json`: 3, 8, 14, 18
  - `appsettings.Production.json`: 3
- Description:
  SQL credentials, Graph client secrets, Azure AD client secrets, and Deltek DSN credentials are stored in tracked configuration files. The SQL connection string also enables `TrustServerCertificate=True`, which weakens transport validation. This creates immediate credential exposure and increases rotation and audit risk.
- Recommended fix:
  Revoke and rotate all exposed secrets immediately. Remove secrets from source control and repository history, move them to environment variables or a secure secret store, replace tracked values with placeholders, and remove `TrustServerCertificate=True` outside local development.

## 2. Public Inspections Endpoint Can Be Queried Without Verification

- Severity: Critical
- File paths:
  - `Kor.Inspections.App/Pages/Inspections/ByProject.cshtml`
  - `Kor.Inspections.App/Pages/Index.cshtml.cs`
  - `Kor.Inspections.App/Pages/Inspections/ByProject.cshtml.cs`
- Line numbers:
  - `ByProject.cshtml`: 99-106
  - `Index.cshtml.cs`: 334-382
  - `ByProject.cshtml.cs`: 8-16
- Description:
  The public inspections page displays an `Email` query value, but the client fetch only posts `projectNumber`. On the server, verification is skipped when the email is absent, so anyone who knows a project prefix can retrieve inspection data for that project.
- Recommended fix:
  Require a verified principal for `LookupInspections`, reject requests with missing email, bind the page to a server-side verified session instead of trusting client-supplied query and body values, and add integration tests for missing-email and mismatched-email access attempts.

## 3. One Successful Code Verification Permanently Trusts An Entire Email Domain For A Project

- Severity: High
- File paths:
  - `Kor.Inspections.App/Services/ProjectBootstrapVerificationService.cs`
- Line numbers:
  - 47-89
  - 109-148
  - 204-239
- Description:
  Verification is implemented at the domain level rather than the individual user level. A single successful 6-digit verification grants persistent trust for the entire domain on that project and stores that trust in `ProjectDefaults` without expiry.
- Recommended fix:
  Move trust to the verified user level, or require explicit admin approval plus expiry for domain-level trust. If domain trust remains intentional, add approval metadata, expiration, and revocation rules, and separate temporary verification from long-lived authorization.

## 4. Cancellation Authorization Is Based On Email Domain Suffix, Not The Verified Contact

- Severity: High
- File paths:
  - `Kor.Inspections.App/Pages/Index.cshtml.cs`
- Line numbers:
  - 397-434
- Description:
  After verification, cancellation is authorized by project prefix and contact email domain suffix. Any verified user from the same domain can cancel another person’s booking for that project.
- Recommended fix:
  Authorize cancellation against the exact verified email address or a server-issued booking-specific token or claim. If shared cancellation rights are required, represent them explicitly instead of inferring them from the email domain.

## 5. Public Health Endpoint Exposes Application And Database Reachability

- Severity: Medium
- File paths:
  - `Kor.Inspections.App/Program.cs`
  - `Kor.Inspections.Tests/HealthzEndpointTests.cs`
- Line numbers:
  - `Program.cs`: 198-199
  - `HealthzEndpointTests.cs`: 12-25
- Description:
  `/healthz` is exposed without authorization, and the current tests assert a public `Healthy` response body. This leaks service and database availability to unauthenticated callers.
- Recommended fix:
  Restrict `/healthz` to internal networks, authentication, or a dedicated probe path with redacted output.

## 6. Blocking ODBC Work Is Wrapped In Task.Run

- Severity: Medium
- File paths:
  - `Kor.Inspections.App/Services/DeltekProjectService.cs`
- Line numbers:
  - 51-83
  - 103-159
- Description:
  Synchronous ODBC I/O is offloaded with `Task.Run`. This still consumes thread-pool resources and can degrade throughput or increase latency under concurrent traffic.
- Recommended fix:
  Isolate Deltek access behind bounded concurrency, caching, or a dedicated worker, and prefer a truly async provider if one is available.

## 7. Dominant Booking Queries Lack Supporting Indexes

- Severity: Medium
- File paths:
  - `Kor.Inspections.App/Data/InspectionsContext.cs`
  - `Kor.Inspections.App/Services/BookingService.cs`
  - `Kor.Inspections.App/Pages/Index.cshtml.cs`
  - `Kor.Inspections.App/Pages/Admin/Index.cshtml.cs`
  - `Kor.Inspections.App/Services/TimeRuleService.cs`
- Line numbers:
  - `InspectionsContext.cs`: 54-58
  - `BookingService.cs`: 119-124
  - `Index.cshtml.cs`: 354-358
  - `Admin/Index.cshtml.cs`: 321-383
  - `TimeRuleService.cs`: 164-167
- Description:
  The schema currently indexes `ContactEmail` and one uniqueness rule, but high-use queries filter by `StartUtc`, `Status`, and `ProjectNumber`. These paths are likely to degrade into scans as booking volume grows.
- Recommended fix:
  Add nonclustered indexes for the actual read patterns, especially date-range and project/date filters, and validate with query plans before and after the change.

## 8. Error Handling For Admin Summary Emails Is Inconsistent

- Severity: Medium
- File paths:
  - `Kor.Inspections.App/Pages/Admin/Summary.cshtml.cs`
- Line numbers:
  - 159-168
  - 177-205
  - 244-251
- Description:
  Some admin email paths allow exceptions to bubble out of the request, while the bulk-send path swallows failures and only records them in status messaging. This leads to inconsistent operator behavior and partial-send ambiguity.
- Recommended fix:
  Standardize email send error handling across summary actions, log failures consistently, and return clear operator-facing status without breaking the entire request unexpectedly.

## 9. Booking Page Is Overly Monolithic And Crosses Concerns

- Severity: Medium
- File paths:
  - `Kor.Inspections.App/Pages/Index.cshtml`
  - `Kor.Inspections.App/Pages/Index.cshtml.cs`
- Line numbers:
  - `Index.cshtml`: 24-1320
  - `Index.cshtml.cs`: 334-830
- Description:
  The booking flow mixes markup, large inline JavaScript, AJAX orchestration, verification, contact CRUD, inspection listing, cancellation, and booking creation across one page/view-model pair. This increases regression risk and makes the security boundary harder to reason about.
- Recommended fix:
  Split the page into smaller Razor partials or components, move JavaScript into versioned static modules, and extract booking/contact/inspection operations into focused handlers or controllers.

## 10. Security-Sensitive Public Access Flow Has No Corresponding Automated Tests

- Severity: Medium
- File paths:
  - `Kor.Inspections.App/Pages/Inspections/ByProject.cshtml`
  - `Kor.Inspections.App/Services/ProjectBootstrapVerificationService.cs`
  - `Kor.Inspections.Tests/Pages/ManageModelTests.cs`
  - `Kor.Inspections.Tests/HealthzEndpointTests.cs`
- Line numbers:
  - `ByProject.cshtml`: 97-125
  - `ProjectBootstrapVerificationService.cs`: 47-239
  - `ManageModelTests.cs`: 19-155
  - `HealthzEndpointTests.cs`: 12-25
- Description:
  Tests exist for token cancellation and health checks, but there is no automated coverage for `ByProject` authorization, missing-email behavior, or domain-trust persistence and isolation.
- Recommended fix:
  Add integration tests for inspection lookup authorization, verification lifecycle behavior, and cross-user or cross-domain isolation.

## 11. Dead And Legacy Code Remains In The Active Codebase

- Severity: Low
- File paths:
  - `Kor.Inspections.App/Pages/Index.cshtml.cs`
  - `Kor.Inspections.App/Services/ProjectProfileService.cs`
  - `Kor.Inspections.App/Services/ProjectAccessService.cs`
- Line numbers:
  - `Index.cshtml.cs`: 111-119
  - `ProjectProfileService.cs`: 24-50, 93-119
  - `ProjectAccessService.cs`: 12-125
- Description:
  Unused legacy members and services remain, including `EditContactId`, `IsNewContactMode`, unused profile helpers, and a legacy PIN-based access service only referenced by tests. This adds noise and makes the active authorization model harder to follow.
- Recommended fix:
  Remove genuinely unused members and services, or explicitly mark them obsolete with a clear deprecation and migration plan.

## 12. Dependency Sources Are Not Fully Pinned Or Reproducible

- Severity: Low
- File paths:
  - `Kor.Inspections.App/tests/e2e/package.json`
  - `Kor.Inspections.App/tests/e2e/package-lock.json`
  - `Kor.Inspections.App/Pages/Index.cshtml`
- Line numbers:
  - `package.json`: 11-14
  - `package-lock.json`: 14-24
  - `Index.cshtml`: 4-5, 1318-1320
- Description:
  npm dependencies use floating version ranges, and `flatpickr` is loaded from an unpinned CDN URL without an integrity hash. That weakens reproducibility and supply-chain control.
- Recommended fix:
  Pin exact package versions, treat the lockfile as authoritative, and pin CDN assets to explicit versions with SRI or self-host them.

## 13. Publish Profile Can Leave Stale Files On The Target

- Severity: Low
- File paths:
  - `Kor.Inspections.App/Properties/PublishProfiles/FolderProfile.pubxml`
- Line numbers:
  - 5-7
- Description:
  The publish profile keeps existing target files in place during deployment, which can leave removed endpoints or assets live after release.
- Recommended fix:
  Enable clean deploys for release publishing, or move to an artifact-based deployment process that guarantees target-directory convergence.

## Next Steps

1. Rotate all exposed credentials and secrets immediately, remove them from tracked configuration, and scrub them from repository history.
2. Fix `LookupInspections` so inspection data cannot be retrieved without a verified server-side identity or equivalent trusted authorization state.
3. Redesign the verification and authorization model to stop granting project access at the whole-domain level unless explicitly approved and time-bound.
4. Tighten cancellation authorization so only the verified booking owner or an explicitly authorized party can cancel a booking.
5. Restrict the public health endpoint and review other public operational surfaces for unnecessary information exposure.
6. Add integration tests for inspection lookup authorization, verification lifecycle behavior, and cross-user isolation before making broader refactors.
7. Address scaling risks by improving Deltek access patterns and adding database indexes that support the dominant booking queries.
