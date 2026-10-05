# Handoff for Codex — 2026-10-05

Branch work was pushed to `master` at **`3ea6a57`** (from `77edbea`). This note explains what changed, what must happen for the Railway deploy, and what is still open.

Repo: `C:\yeow\SuperScanner` (GitHub `yaptzingyeow/SuperScanner`). Stack: ASP.NET Core 10 minimal APIs + EF Core/Npgsql (PostgreSQL), Worker service (BackgroundServices), Magick.NET, Google Document AI, Angular 22 (signals, Vitest), Cloudflare R2 object storage, Firebase Auth + App Check.

---

## 1. Deploy checklist (do these first)

1. **Apply database migrations** to production before or as the new API/Worker go live. These are new since the last deploy:
   - `20261005061047_AccountRegion` (account time zone / locale)
   - `20261005061610_PrivacyConsent` (privacy notice consent)

   Command (EF needs the Infrastructure project as startup project):
   ```bash
   dotnet ef database update --project src/ArksScanner.Infrastructure --startup-project src/ArksScanner.Infrastructure
   ```
   Without these, the API errors (scans fail with "We could not start the scan").
2. **Make sure the Worker service is deployed and running** on Railway. It hosts all background jobs, including the new permanent-delete job.
3. **Health check:** in Railway → API service → Settings → Healthcheck Path, set **`/health/ready`**. It returns 503 if the database is unreachable or migrations are pending, so Railway will not switch traffic to a broken deploy.
   - `/health` = process is up (liveness). `/health/ready` = DB reachable + no pending migrations.
   - If the production app DB role cannot read `"__EFMigrationsHistory"`, `/health/ready` reports `Degraded` (HTTP 200) instead of checking migrations. Fix with:
     ```sql
     GRANT SELECT ON "__EFMigrationsHistory" TO <app_role>;
     ```
4. **After deploy, run the smoke test:**
   ```bash
   tools/smoke/smoke.sh https://<api-host> https://<web-host>
   ```
   Checks: `/health` 200, `/health/ready` 200, `/api/documents` 401 without sign-in, web root loads with `<app-root>`.

---

## 2. What changed (by area)

### Multicultural / i18n
- **UI languages:** English, Malay (`ms`), Chinese Simplified (`zh`), Arabic (`ar`, right-to-left). Dictionaries in `apps/web/src/app/core/i18n/lang/{en,ms,zh,ar}.ts` (~980 keys each). English is bundled; other languages are lazy-loaded chunks. `I18nService.t(key, params)`; spec `i18n.service.spec.ts` enforces key + `{placeholder}` parity across languages — **add every new key to all four files**.
- Translated: all user screens, editor, admin portal (`admin.*` keys), watermark quick texts/colours/font style names, locale-aware dates.
- **Translation review:** `docs/translation-review/{ms-malay,zh-chinese,ar-arabic}.csv` + README — machine translations waiting for native-speaker review. Apply returned corrections to the `lang/*.ts` files.
- **Multilingual OCR:** `Ocr:Language` default `"auto"` (Document AI detects language; hints supported).
- **World-script fonts:** `assets/fonts/manifest.json` (faces with `scripts` ISO 15924 lists). Text replacement picks fonts that cover the text's scripts (`TextScripts` in Application).
- **Watermark font fallback:** `PdfUserWatermark.FontPath` falls back to a manifest font that covers the text (e.g. Noto Sans SC for Chinese, Noto Naskh Arabic) — previously non-Latin watermarks rendered as empty boxes.
- **Per-user time zone/locale:** web sends `X-Time-Zone`; daily usage resets in the user's zone (change takes effect at most once per 24 h).

### Privacy
- `GET /api/me/export` (download my data), `POST /api/me/consent`, `DELETE /api/me` (body `{ "confirm": "DELETE" }`; deletes Firebase sign-in too; last admin is blocked). Web page `/privacy` + one-time consent banner.

### Documents
- Rename (`PATCH /api/documents/{id}`), delete (`DELETE`, soft remove with reason `user_deleted`).
- **Recycle bin:** `GET /api/documents/bin` (owner-deleted within 30 days), `POST /api/documents/{id}/restore` (404 outside window / not owner / removed by retention; 429 `plan_limit_reached` if over the document limit). Web: "Recently deleted" section on the documents page (loaded on demand).
- **Permanent purge (new Worker job):** `DocumentPurge` + `DocumentPurgeWorker` (every 6 h, batches of 50). Erases documents with `RemovedAt` older than `Retention:PurgeAfterDays` (default **30**) — any reason (user, retention, account deletion): deletes R2 objects first (page original/preview/thumbnail/crop source, revisions, repair previews, exports, upload quarantine/accepted keys), then DB rows in a fixed order to get past RESTRICT foreign keys (revisions ↔ text edits, OCR elements self-ref, upload intents → pages). Audit events are kept. Failures are logged per document and retried next run.

### Signatures
- **Background removal moved to the server:** `POST /api/signatures/prepare` (multipart: `image` PNG/JPEG ≤5 MB, `strength` 0–1, `keepOriginal` bool) → transparent PNG, nothing stored. 422 with `code`: `signature_invalid_image`, `signature_too_large` (>12 MP), `signature_no_ink`.
- Algorithm (`src/ArksScanner.Infrastructure/Signatures/SignatureBackground.cs`): estimates paper brightness per block (90th percentile, bilinear-interpolated), keeps pixels darker than local paper, white-balances ink, trims to ink + margin, resizes to ≤2000 px.
- Web `signature-creator` calls it via `PageSignatureService.prepare` (slider debounced 250 ms). The old client-side `removeLightBackground` was removed.

### Admin
- Users list shows **"No email"** + short UID when an account has no email (phone/no-email sign-in) instead of the raw Firebase UID.

### Ops / tests
- Fixed stale migration tests (they now insert old rows via a helper that temporarily adds later columns).
- Poppler tests use `[PopplerFact]` — skipped when `pdfinfo` is not installed (they run in the Worker Docker image).
- Web Vitest timeout raised to 15 s (`apps/web/vitest-base.config.ts`).
- `*.sh` forced to LF via `.gitattributes`.

---

## 3. Test status at `3ea6a57`

| Suite | Result |
|---|---|
| Domain | 162 passed |
| Application | 526 passed, 3 skipped (Poppler not installed locally, billable live OCR) |
| Infrastructure (Testcontainers) | 121 passed + 2 purge tests passed (before Docker went down) |
| API (Testcontainers) | 173 passed on the previous commit; **not re-run for the last commit** (Docker Desktop stopped) |
| Web (Vitest) | 378 passed; `ng build` OK (~990 kB initial) |

**Please re-run the API suite** (needs Docker running) — it includes the new test `PageSignatureEndpointsTests.Prepare_returns_a_transparent_png_and_explains_unusable_images`, which has never run yet:
```bash
dotnet test tests/ArksScanner.Api.IntegrationTests
```

Web tests need Node ≥ 22.22.3 (the repo has one at `.task-tools/node-v22.22.3-win-x64/node.exe`):
```bash
cd apps/web && ../../.task-tools/node-v22.22.3-win-x64/node.exe node_modules/@angular/cli/bin/ng.js test --watch=false
```

---

## 4. Open items / suggestions

1. Run the API integration suite (above) and fix anything it finds in the new `/api/signatures/prepare` endpoint.
2. Verify on production after deploy: upload a phone photo of a signature → preview shows transparent background; delete a document → appears in "Recently deleted" → restore works.
3. Apply native-speaker corrections from `docs/translation-review/*.csv` when they come back.
4. Arabic text in the **text-replacement editor** was not visually re-checked (watermark Arabic was; same Magick/raqm engine).
5. Backlog ideas (not started): camera capture on phones, document thumbnails in the list, full-text search over OCR text, share link / email export, HitPay subscriptions when the free period ends (plan limits already exist, switch via admin settings `EnforceFromUtc`).

## 5. Conventions

- TDD: write the failing test first; .NET tests use xUnit + Testcontainers (`postgres:17-alpine`).
- Do not hard-code UI text — use `i18n.t` and add keys to all four dictionaries.
- Local services: `.task-tools/start-local-api-secure.ps1` / `start-local-worker-secure.ps1 -Detach -UseStoredPassword` (API :5093, Worker :5094, web dev :4201). Stop them before building (they lock DLLs).
- The user applies database migrations themselves; never type their passwords.
