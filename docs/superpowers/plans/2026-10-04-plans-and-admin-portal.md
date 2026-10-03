# Plans, Usage Limits and Admin Portal Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Free/Pro plans with daily usage limits, document retention and a database-backed admin portal, all switchable from a Test phase to an Enforced phase without code changes.

**Architecture:** A pure domain `EntitlementPolicy` decides limits from plan settings, the account's subscriptions and the phase. An application `PlanService` loads those (settings cached 30 s), atomically consumes daily usage and is called by the OCR, export and document-create paths. Admin features are minimal-API endpoints behind an `Admin` policy plus an Angular `/admin` area; a worker job applies retention.

**Tech Stack:** ASP.NET Core 10 minimal APIs, EF Core 10 + Npgsql (PostgreSQL 17), xUnit + Testcontainers, Angular 22 standalone components with signals, Vitest.

**Spec:** `docs/superpowers/specs/2026-10-03-plans-and-admin-portal-design.md`

## Global Constraints

- Phases: `Test` (everyone Pro limits, brand stamp on, no deletion) and `Enforced`; optional `EnforceFromUtc` makes the effective phase `Enforced` from that instant.
- Free (Enforced): `FreeOcrPagesPerDay` = 5, `FreeWatermarkExportsPerDay` = 3, `FreeMaxDocuments` = 30, `FreeRetentionDays` = 7, brand stamp on.
- Pro: unlimited OCR (no cap), unlimited watermark exports, no brand stamp, no retention, no document limit.
- Usage day = calendar date in `UsageTimeZone`, default `Asia/Kuala_Lumpur`. `OcrCostPerThousandPages` default 1.50 (USD).
- Plan settings are stored in the database and edited in the admin portal; the `Plans` configuration section only seeds the first row. Edits take effect within 30 s.
- Limit refusal: HTTP 429, problem `code` = `plan_limit_reached`, extensions `kind` (`ocr` | `watermark` | `documents`), `limit`, `used`, `resetsAt` (ISO 8601, null for documents).
- Admin: signed-in, non-guest account present in `admins`; owner seeded from config `Admin:OwnerEmail`; the last admin can never be removed.
- Manual grants: durations `1m`, `3m`, `1y`, `forever`, optional note ≤ 200 chars. Payments are read-only.
- Audit event names: `admin.pro_granted`, `admin.pro_revoked`, `admin.pro_extended`, `admin.admin_added`, `admin.admin_removed`, `admin.settings_changed`, `document.expired_removed`.
- Copy: sentence case; limit messages exactly "You've used today's {limit} free OCR pages — upgrade to Pro or come back tomorrow." and "You've used today's {limit} free watermark exports — upgrade to Pro or come back tomorrow."; documents: "The Free plan keeps up to {limit} documents. Delete some or upgrade to Pro."; Upgrade button label "Pro is coming soon".
- Out of scope: HitPay, ads, store billing, email.

## Review Focus

1. Two OCR requests at the same instant when one page remains → exactly one succeeds (Task 4 concurrency test).
2. A user upgraded to Pro one second before the retention job runs → documents survive (Task 6 test).
3. Day rollover at 00:00 Asia/Kuala_Lumpur, not UTC midnight → counters reset at 16:00 UTC (Task 1 test).
4. Removing yourself when you are the only admin, or removing the last admin by UID → refused with 409 (Task 7 test).
5. Test phase with `EnforceFromUtc` in the past → behaves as Enforced without anyone editing settings (Task 1 test).

---

## File Structure

| Area | Files |
|---|---|
| Domain | `src/ArksScanner.Domain/Plans/{PlanSettings,Subscription,Account,AdminMember,UsageDay,Payment,EntitlementPolicy}.cs` |
| Application | `src/ArksScanner.Application/Plans/{IPlanRepository,PlanService,PlanLimitExceededException,AdminQueries,AdminCommands}.cs` |
| Infrastructure | `src/ArksScanner.Infrastructure/Persistence/Configurations/{Account,Subscription,AdminMember,PlanSettings,UsageDay,Payment}Configuration.cs`, `Persistence/EfPlanRepository.cs`, migration `PlansAndAdmin`, `Processing/DocumentRetention.cs` |
| API | `src/ArksScanner.Api/Auth/AccountTrackingMiddleware.cs`, `Endpoints/PlanEndpoints.cs`, `Endpoints/AdminEndpoints.cs`, `Program.cs` |
| Worker | `src/ArksScanner.Worker/DocumentRetentionWorker.cs`, `Program.cs` |
| Web | `apps/web/src/app/plans/{plan.service.ts,plan.models.ts,usage-line.component.ts,limit-message.ts}`, `apps/web/src/app/admin/*` |
| Scripts/docs | `scripts/database/Grant-ApplicationPermissions.ps1`, `docs/operations/plans-and-admin.md` |

---

### Task 1: Domain model and entitlement policy

**Files:**
- Create: `src/ArksScanner.Domain/Plans/PlanSettings.cs`, `Subscription.cs`, `Account.cs`, `AdminMember.cs`, `UsageDay.cs`, `Payment.cs`, `EntitlementPolicy.cs`
- Test: `tests/ArksScanner.Domain.Tests/Plans/EntitlementPolicyTests.cs`, `PlanSettingsTests.cs`, `SubscriptionTests.cs`

**Interfaces:**
- Produces:
  - `enum PlanPhase { Test, Enforced }`, `enum PlanKind { Free, Pro }`, `enum UsageKind { Ocr, Watermark }`, `enum SubscriptionSource { Manual, HitPay, AppStore, GooglePlay }`, `enum SubscriptionStatus { Active, Revoked, Ended }`
  - `sealed class PlanSettings` (single row, `Id` = 1): `Phase`, `EnforceFromUtc?`, `FreeOcrPagesPerDay`, `FreeWatermarkExportsPerDay`, `FreeMaxDocuments`, `FreeRetentionDays`, `UsageTimeZone`, `OcrCostPerThousandPages` (decimal), `UpdatedAt`, `UpdatedByUid?`; `static PlanSettings Seed(PlanSettingsValues, now)`; `void Update(PlanSettingsValues, string adminUid, now)` (validates: OCR 0–10000, watermark 0–10000, documents 1–100000, retention 1–3650, cost 0–1000, valid IANA zone via `TimeZoneInfo.FindSystemTimeZoneById`); `PlanPhase EffectivePhase(DateTimeOffset now)`; `DateOnly UsageDay(DateTimeOffset now)`; `DateTimeOffset NextReset(DateTimeOffset now)`.
  - `sealed record PlanSettingsValues(PlanPhase Phase, DateTimeOffset? EnforceFromUtc, int FreeOcrPagesPerDay, int FreeWatermarkExportsPerDay, int FreeMaxDocuments, int FreeRetentionDays, string UsageTimeZone, decimal OcrCostPerThousandPages)`
  - `sealed class Subscription`: `static Subscription GrantManual(Guid id, string accountUid, DateTimeOffset startsAt, DateTimeOffset? endsAt, string? note, string grantedByUid, now)`; `bool IsActiveAt(DateTimeOffset now)`; `void Revoke(string adminUid, now)`; `void Extend(DateTimeOffset? newEndsAt, now)` (null = forever; must be later than current end).
  - `sealed class Account`: `FirebaseUid`, `Email?`, `SignInProvider`, `IsGuest`, `CreatedAt`, `LastSeenAt`, `RetentionGraceFrom?`; `static Account Create(...)`; `bool Touch(email, provider, isGuest, now)` returns true when `LastSeenAt` was older than 10 minutes or identity fields changed.
  - `sealed class AdminMember(AccountUid, AddedByUid?, AddedAt)`, `sealed class UsageDay(AccountUid, Day, OcrPages, WatermarkExports, BonusOcrPages)`, `sealed class Payment` (fields per spec, no behaviour).
  - `sealed record Entitlements(PlanKind Plan, PlanPhase Phase, bool BrandStamp, int? OcrPagesPerDay, int? WatermarkExportsPerDay, int? MaxDocuments, int? RetentionDays, DateTimeOffset? ProUntil, bool ProForever)` — `null` limit = unlimited.
  - `static class EntitlementPolicy { Entitlements Evaluate(PlanSettings settings, IReadOnlyCollection<Subscription> subscriptions, DateTimeOffset now) }`

- [ ] **Step 1: Write the failing tests** (`EntitlementPolicyTests`)
  - `Test_phase_gives_everyone_unlimited_use_but_keeps_the_brand_stamp`: Free account, phase Test → `OcrPagesPerDay` null, `WatermarkExportsPerDay` null, `MaxDocuments` null, `RetentionDays` null, `BrandStamp` true, `Plan` Free.
  - `Enforced_free_account_gets_the_configured_limits`: defaults → 5, 3, 30, 7, `BrandStamp` true.
  - `Pro_is_unlimited_without_brand_stamp_or_retention`: active manual subscription → all null, `BrandStamp` false, `Plan` Pro, `ProUntil` = subscription end.
  - `Forever_grant_reports_pro_forever`, `Revoked_or_expired_subscription_is_free`, `Subscription_starting_in_the_future_is_free_until_it_starts`.
  - `EnforceFromUtc_in_the_past_switches_a_test_phase_to_enforced` (Review Focus 5).
  - `PlanSettingsTests.Usage_day_rolls_over_at_midnight_in_Kuala_Lumpur`: `2026-10-04T15:59:59Z` → day 2026-10-04, `16:00:00Z` → 2026-10-05; `NextReset(15:00Z)` = `2026-10-04T16:00:00Z` (Review Focus 3).
  - `PlanSettingsTests.Update_rejects_out_of_range_values_and_unknown_time_zones` (theory over each bound).
  - `SubscriptionTests.Extend_must_move_the_end_later_and_null_means_forever`.

- [ ] **Step 2: Run** `dotnet test tests/ArksScanner.Domain.Tests --artifacts-path .task-tools/test-artifacts --filter "FullyQualifiedName~Plans"` — Expected: FAIL (types missing).
- [ ] **Step 3: Implement** the types above. Pro = any subscription with `IsActiveAt(now)`; `ProUntil` = latest end among active ones; forever if any active one has no end.
- [ ] **Step 4: Run** the same command — Expected: PASS.
- [ ] **Step 5: Commit** `feat: plan domain model and entitlement policy`.

### Task 2: Persistence, migration and grants

**Files:**
- Create: the six `*Configuration.cs` files under `src/ArksScanner.Infrastructure/Persistence/Configurations/`, migration `src/ArksScanner.Infrastructure/Persistence/Migrations/<timestamp>_PlansAndAdmin.cs`
- Modify: `Persistence/AppDbContext.cs` (DbSets `Accounts`, `Subscriptions`, `Admins`, `PlanSettings`, `UsageDays`, `Payments`), `src/ArksScanner.Domain/Documents/Document.cs` (add `RemovedAt?`, `RemovedReason?`, `void Remove(string reason, DateTimeOffset now)` which soft-removes the document and all active pages), `scripts/database/Grant-ApplicationPermissions.ps1`
- Test: `tests/ArksScanner.Infrastructure.IntegrationTests/Persistence/PlansPersistenceTests.cs`

**Interfaces:**
- Consumes: Task 1 types.
- Produces: tables `accounts` (PK `FirebaseUid`), `subscriptions` (index `AccountUid, Status`), `admins` (PK `AccountUid`), `plan_settings` (PK `Id`, check `Id = 1`), `usage_days` (PK `AccountUid, Day`), `payments` (unique `Provider, ProviderReference`); `documents.RemovedAt`, `documents.RemovedReason`.

- [ ] **Step 1: Write failing tests**: `Migration_backfills_one_account_per_existing_document_owner` (seed two documents for `u1`, one for `u2` before applying the new migration → 2 accounts, `SignInProvider` = `unknown`); `Plan_entities_round_trip`; `Only_one_plan_settings_row_is_allowed` (insert `Id = 2` → `DbUpdateException`); `Removed_documents_keep_their_rows_and_pages`.
- [ ] **Step 2: Run** `dotnet test tests/ArksScanner.Infrastructure.IntegrationTests --artifacts-path .task-tools/test-artifacts --filter "FullyQualifiedName~PlansPersistence"` — Expected: FAIL.
- [ ] **Step 3: Implement** configurations, `dotnet ef migrations add PlansAndAdmin --project src/ArksScanner.Infrastructure --startup-project src/ArksScanner.Infrastructure`; append backfill SQL `INSERT INTO accounts ("FirebaseUid","SignInProvider","IsGuest","CreatedAt","LastSeenAt") SELECT "OwnerFirebaseUid",'unknown',false,min("CreatedAt"),max("UpdatedAt") FROM documents GROUP BY "OwnerFirebaseUid" ON CONFLICT DO NOTHING;`. Add the six tables to the grants list (`payments` SELECT only; `plan_settings`, `admins` SELECT/INSERT/UPDATE/DELETE).
- [ ] **Step 4: Run** — Expected: PASS. Also run the existing `PageRevisionPersistenceTests` and record that its two pre-existing `Rotation` failures are unchanged.
- [ ] **Step 5: Commit** `feat: plans, accounts and admin persistence`.

### Task 3: Account tracking, admin seeding and Admin policy

**Files:**
- Create: `src/ArksScanner.Api/Auth/AccountTrackingMiddleware.cs`, `src/ArksScanner.Application/Plans/AdminBootstrap.cs`
- Modify: `src/ArksScanner.Api/Auth/AuthPolicies.cs` (add `public const string Admin = "Admin"`), `FirebaseAuthenticationHandler.cs` (add claims `email`, `sign_in_provider`), `src/ArksScanner.Infrastructure/Auth/FirebaseAdminIdTokenVerifier.cs` (+`SignInProvider`), `Program.cs`, `appsettings.json` (`"Admin": { "OwnerEmail": "" }`)
- Test: `tests/ArksScanner.Api.IntegrationTests/Plans/AccountTrackingTests.cs`, `AdminPolicyTests.cs`

**Interfaces:**
- Produces: `IAdminDirectory.IsAdminAsync(string uid, CancellationToken)` (cached 30 s per uid); `AdminBootstrap.EnsureOwnerAsync(string? ownerEmail)` runs at API start and after each account upsert whose email equals `Admin:OwnerEmail` (case-insensitive) while `admins` is empty.

- [ ] **Step 1: Failing tests**: `First_authenticated_request_creates_the_account_and_repeat_requests_within_10_minutes_do_not_write`; `Owner_email_becomes_the_first_admin`; `Admin_endpoints_return_404_for_non_admins_and_guests` (use `GET /api/admin/dashboard`, implemented in Task 7 — for now map a placeholder `GET /api/admin/ping` that Task 7 removes); `Admin_endpoints_allow_admins`.
- [ ] **Step 2: Run** API integration tests filter `Plans` — FAIL.
- [ ] **Step 3: Implement**: middleware after authentication, best-effort (failure logs `account_tracking_failed`, request continues); `Admin` policy = authenticated, no guest claim, `IAdminDirectory.IsAdminAsync`; failed admin authorization returns 404 (not 403) via an endpoint filter.
- [ ] **Step 4: Run** — PASS. **Step 5: Commit** `feat: account tracking and admin policy`.

### Task 4: Plan service, atomic usage and /api/me/plan

**Files:**
- Create: `src/ArksScanner.Application/Plans/IPlanRepository.cs`, `PlanService.cs`, `PlanLimitExceededException.cs`, `src/ArksScanner.Infrastructure/Persistence/EfPlanRepository.cs`, `src/ArksScanner.Api/Endpoints/PlanEndpoints.cs`
- Modify: `Program.cs` (API and Worker registrations)
- Test: `tests/ArksScanner.Application.Tests/Plans/PlanServiceTests.cs`, `tests/ArksScanner.Infrastructure.IntegrationTests/Persistence/UsageConcurrencyTests.cs`, `tests/ArksScanner.Api.IntegrationTests/Plans/MePlanTests.cs`

**Interfaces:**
- Consumes: Task 1 policy, Task 2 tables.
- Produces:
  - `PlanService.GetEntitlementsAsync(string uid, CancellationToken) -> Task<Entitlements>`
  - `PlanService.ConsumeAsync(string uid, UsageKind kind, int amount, CancellationToken)` — throws `PlanLimitExceededException(UsageKind? Kind, string KindCode, int Limit, int Used, DateTimeOffset? ResetsAt)`; no-op counter increment still recorded when unlimited.
  - `PlanService.EnsureCanCreateDocumentAsync(string uid, CancellationToken)` — throws with `KindCode` `documents`.
  - `PlanService.GetUsageAsync(string uid, CancellationToken) -> Task<PlanUsage>`; `record PlanUsage(DateOnly Day, int OcrPages, int BonusOcrPages, int WatermarkExports, DateTimeOffset ResetsAt)`
  - `IPlanRepository.TryConsumeAsync(string uid, DateOnly day, UsageKind kind, int amount, int? limit, CancellationToken) -> Task<int?>` (new used count, null when refused) implemented with `INSERT … ON CONFLICT ("AccountUid","Day") DO UPDATE SET … WHERE usage_days.<col> + @amount <= @limit + usage_days."BonusOcrPages"` (bonus only for OCR) `RETURNING`.
  - `GET /api/me/plan` → `{ plan, phase, proUntil, proForever, brandStamp, limits: { ocrPagesPerDay, watermarkExportsPerDay, maxDocuments, retentionDays }, usage: { ocrPages, bonusOcrPages, watermarkExports, resetsAt }, documentCount }`
  - A shared helper `PlanProblem.From(PlanLimitExceededException)` returning the 429 problem (Global Constraints shape).

- [ ] **Step 1: Failing tests**: `Free_user_can_use_exactly_five_ocr_pages_then_is_refused_with_reset_time`; `Bonus_pages_raise_the_daily_allowance`; `Test_phase_never_refuses_but_still_counts`; `Settings_are_cached_and_refreshed_after_30_seconds` (fake clock); concurrency: `Twenty_parallel_consumes_with_one_page_left_succeed_exactly_once` (Review Focus 1); API: `Me_plan_returns_limits_usage_and_reset`.
- [ ] **Step 2: Run** — FAIL. **Step 3: Implement.** **Step 4: Run** — PASS.
- [ ] **Step 5: Commit** `feat: plan service with atomic daily usage`.

### Task 5: Enforcement in OCR, exports and document creation

**Files:**
- Modify: `src/ArksScanner.Application/Ocr/RequestPageOcr.cs` (consume 1 `Ocr` only when a result is newly queued or a failed one is retried — inside the existing transaction, before enqueue), `src/ArksScanner.Api/Endpoints/OcrEndpoints.cs`, `src/ArksScanner.Application/Documents/CreateDocumentExport.cs` (consume 1 `Watermark` when a watermark is present; snapshot `BrandStamp` from entitlements), `src/ArksScanner.Domain/Documents/DocumentExport.cs` (`DocumentExportSnapshotEntry` + `bool BrandStamp = true`), `src/ArksScanner.Infrastructure/Processing/DocumentPdfBuilder.cs` (stamp only when `PdfExportOptions.BrandWatermark && entry.BrandStamp`), `src/ArksScanner.Application/Documents/CreateDocument.cs` + the import/upload path that creates documents, `DocumentExportEndpoints.cs`, `DocumentsEndpoints.cs`
- Test: extend `RequestPageOcrTests`, `DocumentExportEndpointsTests`, `DocumentPdfBuilderTests`, `DocumentsEndpointsTests`

**Interfaces:**
- Consumes: Task 4 `PlanService`, `PlanProblem`.

- [ ] **Step 1: Failing tests**: `Sixth_free_ocr_request_in_a_day_returns_429_plan_limit_reached`; `Reading_existing_ocr_and_carried_forward_results_do_not_count`; `Fourth_watermarked_export_returns_429_but_plain_export_still_works`; `Pro_export_snapshot_has_no_brand_stamp_and_pdf_has_one_image_per_page`; `Thirty_first_document_returns_429_documents_limit`.
- [ ] **Step 2: Run** — FAIL. **Step 3: Implement.** **Step 4: Run** the four test classes plus the full API integration suite — PASS.
- [ ] **Step 5: Commit** `feat: enforce plan limits for OCR, watermarks and documents`.

### Task 6: Document retention

**Files:**
- Create: `src/ArksScanner.Infrastructure/Processing/DocumentRetention.cs`, `src/ArksScanner.Worker/DocumentRetentionWorker.cs`
- Modify: `src/ArksScanner.Application/Documents/ListDocuments.cs` (exclude removed documents; add `ExpiresAt?` to `DocumentSummary`), document detail/repository queries (removed documents return 404), `src/ArksScanner.Worker/Program.cs`, `PlanService` (grace: when Pro ends or effective phase becomes Enforced, set `Account.RetentionGraceFrom = now` if null — evaluated in `GetEntitlementsAsync` when the account's previous plan, cached on the account as `LastPlan`, differs)
- Test: `tests/ArksScanner.Infrastructure.IntegrationTests/Processing/DocumentRetentionTests.cs`, `tests/ArksScanner.Api.IntegrationTests/Documents/DocumentsEndpointsTests.cs`

**Interfaces:**
- Produces: `DocumentRetention.RunAsync(CancellationToken) -> Task<int>` (documents removed). Expiry = `max(CreatedAt, RetentionGraceFrom ?? CreatedAt) + FreeRetentionDays`. Each removal re-loads entitlements inside its transaction, calls `Document.Remove("retention", now)` and audits `document.expired_removed`; object cleanup reuses the existing retired-asset paths.

- [ ] **Step 1: Failing tests**: `Expired_free_documents_are_removed_and_audited`; `Pro_or_test_phase_documents_are_never_removed`; `Account_upgraded_just_before_the_run_keeps_its_documents` (Review Focus 2); `Grace_gives_old_documents_fresh_days_when_pro_ends`; `Removed_documents_disappear_from_the_list_and_return_404`; `List_shows_expiry_for_free_documents_in_enforced_phase`.
- [ ] **Step 2: Run** — FAIL. **Step 3: Implement** (worker runs hourly like `PageRepairCleanupWorker`). **Step 4: Run** — PASS.
- [ ] **Step 5: Commit** `feat: free-plan document retention`.

### Task 7: Admin API

**Files:**
- Create: `src/ArksScanner.Application/Plans/AdminQueries.cs`, `AdminCommands.cs`, `src/ArksScanner.Api/Endpoints/AdminEndpoints.cs`
- Modify: `Program.cs` (remove the Task 3 ping endpoint)
- Test: `tests/ArksScanner.Api.IntegrationTests/Plans/AdminEndpointsTests.cs`

**Interfaces:**
- Consumes: Tasks 1–6.
- Produces (all under `/api/admin`, `Admin` policy, 50 per page, `no-store`):
  - `GET dashboard` → `{ users: { total, signedIn, guests, new: { today, d7, d30 }, active7d }, subscribers: { total, manual, paid }, documents: { total, today }, ocr: { today, month, estimatedCostMonth }, watermarkExports: { today, month }, series: [{ day, newUsers, ocrPages }] (30 days), phase, effectivePhase, enforceFromUtc }`
  - `GET users?query=&page=` / `GET users/{uid}`; `POST users/{uid}/subscriptions` `{ duration: "1m"|"3m"|"1y"|"forever", note? }`; `GET subscriptions`; `POST subscriptions/{id}/revoke`; `POST subscriptions/{id}/extend` `{ duration }`; `GET payments`; `GET|PUT settings`; `GET admins`; `POST admins` `{ email }` (404 when no account with that email); `DELETE admins/{uid}` (409 `last_admin` when it would leave none); `GET audit?limit=` (admin.* events only, default 20, max 100).
  - Every mutation appends the matching audit event with `{ old, new }` JSON (settings) or `{ accountUid, subscriptionId, duration }`.

- [ ] **Step 1: Failing tests**: `Dashboard_counts_users_subscribers_and_ocr_cost`; `Grant_extend_and_revoke_pro_change_entitlements_and_are_audited`; `Settings_update_validates_and_takes_effect`; `Cannot_remove_the_last_admin` (Review Focus 4); `Add_admin_requires_an_existing_account`; `Payments_list_is_empty_until_hitpay`; `Non_admin_gets_404_on_every_admin_route`.
- [ ] **Step 2: Run** — FAIL. **Step 3: Implement.** **Step 4: Run** the full API integration suite — PASS.
- [ ] **Step 5: Commit** `feat: admin API`.

### Task 8: Plan awareness in the web app

**Files:**
- Create: `apps/web/src/app/plans/plan.models.ts`, `plan.service.ts` (signal store over `GET /api/me/plan`, refresh after OCR/export/document actions and on 429), `usage-line.component.ts`, `limit-message.ts` (`limitMessage(problem): string` using the Global Constraints copy)
- Modify: `apps/web/src/app/documents/page-text-editor.component.*` (usage line "OCR today: {used} / {limit}" when a limit exists, limit message on 429), `document-workspace.component.*` (search Recognize), `export-status.component.*` and `watermark-panel.component.*` ("Watermarks today: {used} / {limit}"), document list cards ("Deletes in {n} days", banner when ≤ 1 day), `layout/app-shell.component.*` (account menu shows "Free", "Pro until {date}" or "Pro (forever)", button "Pro is coming soon")
- Test: matching `*.spec.ts`

- [ ] **Step 1: Failing tests**: `shows OCR usage and the limit message after a 429`; `hides usage lines when limits are unlimited`; `export shows watermark usage and keeps plain export available at the limit`; `document card shows days until deletion`; `account menu shows plan and coming-soon upgrade`.
- [ ] **Step 2: Run** `node node_modules/@angular/cli/bin/ng.js test --watch=false` (from `apps/web`) — FAIL. **Step 3: Implement.** **Step 4: Run** — PASS.
- [ ] **Step 5: Commit** `feat: show plan usage and limits in the app`.

### Task 9: Admin portal UI

**Files:**
- Create: `apps/web/src/app/admin/admin.routes.ts`, `admin-shell.component.*` (left menu), `admin-api.service.ts`, `admin.guard.ts` (calls `GET /api/admin/dashboard`; any failure → not-found page), `dashboard.component.*` (metric cards + 30-day SVG bar chart, phase banner), `users.component.*`, `user-detail.component.*` (Give Pro with duration select + note, Revoke), `subscribers.component.*` (Extend, Revoke), `payments.component.*` (empty state "Payments appear here once HitPay is connected."), `settings.component.*` (all values, confirm "Apply to all users now?"), `admins.component.*` (add by email, remove; last-admin message)
- Modify: `apps/web/src/app/app.routes.base.ts` (lazy `/admin` children), `layout/app-shell.component.*` (Admin link when `GET /api/me/plan` reports `isAdmin: true` — add `isAdmin` to the Task 4 response in this task), destructive actions use `window.confirm`
- Test: one spec per page component plus `admin.guard.spec.ts`

- [ ] **Step 1: Failing tests**: guard redirects non-admins; dashboard renders counts and phase banner; give-Pro posts `{ duration: '3m', note }`; settings save confirms then PUTs; remove-admin shows the last-admin error from 409; payments empty state.
- [ ] **Step 2: Run** — FAIL. **Step 3: Implement** with the existing workspace look (`_toolbar.scss` tokens, `app-icon`). **Step 4: Run** full web suite and `ng build` — PASS.
- [ ] **Step 5: Commit** `feat: admin portal`.

### Task 10: Operations doc and end-to-end verification

**Files:**
- Create: `docs/operations/plans-and-admin.md` (phase switch, settings, granting Pro, adding admins, retention, recovery)
- Modify: `.task-tools/start-local-api-secure.ps1` (set `Admin__OwnerEmail`)

- [ ] **Step 1:** Run all suites: Domain, Application, Python worker, API integration, Infrastructure integration, web tests, `ng build`, Docker builds of api/worker/web. Expected: all pass except the documented pre-existing ones (2 Poppler, 2 `Rotation` migration tests).
- [ ] **Step 2:** Restart API and Worker, verify `/api/documents` returns 200, then in the browser: admin link visible for the owner; dashboard numbers; grant Pro to a test account; switch to Enforced in settings; a Free test account hits 5 OCR pages and sees the message; switch back to Test.
- [ ] **Step 3: Commit** `docs: plans and admin operations`.
