# Plans and admin portal — operations

How to run the Free/Pro plans and the admin portal (`/admin`). The design is in
`docs/superpowers/specs/2026-10-03-plans-and-admin-portal-design.md`.

## First-time setup

1. Apply the `PlansAndAdmin` migration and grant table rights. Both scripts need the PostgreSQL
   owner password.

   ```powershell
   .\scripts\database\Apply-DatabaseMigrations.ps1
   .\scripts\database\Grant-ApplicationPermissions.ps1
   ```

   The migration creates `accounts`, `subscriptions`, `admins`, `plan_settings`, `usage_days` and
   `payments`. It also backfills one account for every existing document owner. The app role gets
   `SELECT` only on `payments`.

2. Set the owner admin in the API configuration: `Admin__OwnerEmail` (or `Admin:OwnerEmail` in
   `appsettings`). While there are no admins, the first visit from a signed-in, non-guest account
   with that email makes it an admin. After that, admins manage admins in the portal. Locally,
   `.task-tools\start-local-api-secure.ps1 -OwnerEmail you@example.com` sets it.

3. Optionally seed the starting plan settings (used only when the `plan_settings` row does not
   exist yet; afterwards the portal is the source of truth). Set these in both the API and the
   Worker:

   | Key | Default |
   | --- | --- |
   | `Plans:Phase` | `Test` |
   | `Plans:EnforceFromUtc` | (none) |
   | `Plans:FreeOcrPagesPerDay` | `5` |
   | `Plans:FreeWatermarkExportsPerDay` | `3` |
   | `Plans:FreeMaxDocuments` | `30` |
   | `Plans:FreeRetentionDays` | `7` |
   | `Plans:UsageTimeZone` | `Asia/Kuala_Lumpur` |
   | `Plans:OcrCostPerThousandPages` | `1.50` |

## Phases

- **Test**: everyone gets Pro features (unlimited OCR and watermarks). The Arks Scanner stamp
  stays on exports and nothing is deleted. Usage is still counted for the dashboard.
- **Enforced**: Free accounts get the daily limits, the document cap and 7-day retention. Pro
  accounts are unlimited, have no stamp, and keep their documents.

To switch, go to **Admin → Settings → Phase**, or set **Switch to Enforced automatically on** to a
date. Changes reach every API and Worker instance within 30 seconds (settings cache).

## Limits

- Daily counters reset at midnight in `UsageTimeZone`. Only new recognitions count: reading an
  existing OCR result, or a result carried forward after text edits, is free.
- Exports count against the watermark limit only when they carry a custom watermark. Plain exports
  are never blocked.
- Over a limit, the API returns `429` with `code: plan_limit_reached` and
  `kind: ocr | watermark | documents`. The web app shows the matching message.

## Giving Pro ("free for friends")

**Admin → Users → (user) → Give Pro.** Choose 1 month, 3 months, 1 year or Forever, and add a note.
**Admin → Subscribers** lists every grant and lets you **Extend** or **Revoke** it. Every grant,
extension and revocation is written to the audit log (`admin.subscription_*`).

## Admins

**Admin → Admins → Add admin** takes the email of an account that has already signed in (guests
cannot be admins). You cannot remove the last admin (`409 last_admin`). Admin rights are cached for
30 seconds per account.

## Retention (Free, Enforced only)

The Worker's hourly `DocumentRetentionWorker` soft-removes Free documents older than
`FreeRetentionDays`. The clock starts at the later of the document's creation and the account's
`RetentionGraceFrom`. Grace is set when Pro ends or when plans become enforced, so nobody loses
documents the moment the phase flips. Each removal re-checks the plan inside the document lock, so
an upgrade just before a run keeps the documents. It is audited as `document.expired_removed`.

Removed documents keep their rows, pages and stored files (`documents.RemovedAt`,
`RemovedReason = 'retention'`), but disappear from every API query.

### Recovering a removed document

```sql
UPDATE documents SET "RemovedAt" = NULL, "RemovedReason" = NULL WHERE "Id" = '<document id>';
UPDATE pages SET "RemovedAt" = NULL, "RemovedByFirebaseUid" = NULL
 WHERE "DocumentId" = '<document id>' AND "RemovedAt" = (SELECT "UpdatedAt" FROM documents WHERE "Id" = '<document id>');
```

Run it as the
database owner, then grant the account Pro (or move it back to Test) so the next hourly run does
not remove the document again.

## Payments

**Admin → Payments** is read-only and stays empty until HitPay is connected (a later phase).
