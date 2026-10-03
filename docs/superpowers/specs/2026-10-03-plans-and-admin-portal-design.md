# Plans, Usage Limits and Admin Portal — Design

Status: approved in conversation on 2026-10-03; written spec for review.

## 1. Intent

Launch Arks Scanner publicly with a free test period, then introduce a paid **Pro** plan without code
changes: the phase and every limit are switched and tuned by an administrator in an **admin portal**.
The portal also shows how many users and subscribers there are, lets an admin give Pro to friends
manually (no payment), shows payments (once HitPay exists) and a dashboard.

Delivered in three pieces; this spec covers 1 and 2:

1. **Plans and limits** — entitlements, daily usage counters, phase, document retention, in-app usage display.
2. **Admin portal** — dashboard, users, subscribers, payments (read-only), settings, admins.
3. **HitPay payments** — separate design later (checkout, webhooks, filling the payments table).

### Decisions taken (verbatim from the conversation)

- Test phase: everyone gets Pro features (unlimited OCR and custom watermarks), but the
  "Scanned with Arks Scanner" stamp stays and nothing is auto-deleted.
- Free limits once enforced (all editable in the admin portal): **5 OCR pages per day**,
  **3 custom-watermark exports per day**, **30 documents maximum**, documents **deleted 7 days after
  creation**.
- Pro: unlimited OCR (no cap; decided 2026-10-03), **no ads in the mobile app**, unlimited custom watermarks,
  **no brand stamp**, documents kept with no time limit.
- Website Free users: daily limits, **no ads**. Mobile Free users (later): watch an ad to unlock OCR / PDF;
  the counters reserve a "bonus from ads" slot now, ads are not built now.
- One account across web and mobile: Pro and daily limits belong to the account (Firebase UID), not the device.
  Guests are device-bound and must log in to carry Pro or documents across devices.
- Admins are stored in the database and **admins manage admins**; the owner's account is seeded as the
  first admin from configuration; the last admin cannot be removed.
- Manual Pro grants choose a duration each time: 1 month, 3 months, 1 year, or forever, with an optional note.
- Payments page is **read-only and empty until HitPay** is connected.

## 2. Plans, phase and limits

### 2.1 Phase

`Test` or `Enforced`, plus an optional `EnforceFromUtc` instant after which the effective phase is
`Enforced`. Both are stored in plan settings (2.4) and edited in the portal.

| Rule | Test | Enforced, Free | Enforced, Pro |
|---|---|---|---|
| OCR pages per day | unlimited | `FreeOcrPagesPerDay` (5) | unlimited |
| Custom-watermark exports per day | unlimited | `FreeWatermarkExportsPerDay` (3) | unlimited |
| Brand stamp on PDF / Print | on | on | **off** |
| Maximum documents | unlimited | `FreeMaxDocuments` (30) | unlimited |
| Document retention | kept | deleted `FreeRetentionDays` (7) days after creation | kept |
| Scan, looks, crop, rotate, clean, export, signatures, ticks | ✅ | ✅ | ✅ |

Guests (anonymous sign-in) are Free. The existing guest policy is unchanged: guests must log in before
editing text, adding signatures or marks.

### 2.2 Entitlements service

A single application service decides what an account may do. Every feature asks it; no rule is
duplicated elsewhere.

```
Entitlements GetEntitlements(accountUid, now)
  -> Plan (Free|Pro), Phase (Test|Enforced), BrandStamp (bool),
     OcrPagesPerDay (int?|null=unlimited), WatermarkExportsPerDay (int?), MaxDocuments (int?),
     RetentionDays (int?), ProUntil (DateTimeOffset?|null=forever)
UsageReservation TryConsume(accountUid, UsageKind kind, int amount, now)  // atomic
UsageSummary GetUsage(accountUid, now)                                     // today's used / limit / bonus
```

- **Pro** = at least one subscription with status Active and `StartsAt <= now < EndsAt` (or no end).
- **Day boundary**: the usage day is the calendar date in `UsageTimeZone` (default `Asia/Kuala_Lumpur`).
- **Counted OCR**: each page recognition request (`RequestPageOcr`) that creates or retries a result
  consumes 1 page. Carried-forward OCR after text edits, reading existing results, and automatic OCR (off)
  never consume.
- **Counted watermark exports**: an export (PDF or Print) created with a custom watermark consumes 1.
- **Bonus**: `BonusOcrPages` on the day row increases the day's OCR allowance (reserved for mobile ads).
- **Refusal**: when a request would exceed the allowance the server returns `429` with problem code
  `plan_limit_reached` and `{ kind, limit, used, resetsAt }`. Pre-existing work is never interrupted.
- **Atomicity**: consumption is a single conditional `INSERT … ON CONFLICT … DO UPDATE … WHERE used + n <= limit`
  so concurrent requests cannot exceed the limit.

### 2.3 Retention and document limit (Free, Enforced)

- Each document of a Free account in the Enforced phase has an **expiry** = `CreatedAt + RetentionDays`,
  computed on read (not stored), so changing `FreeRetentionDays` applies immediately.
- When a Pro period ends (or phase becomes Enforced), documents older than the retention window get a
  **grace expiry** of `now + RetentionDays` (stored per account as `RetentionGraceFrom`), so nothing vanishes
  at once.
- A worker job runs hourly: deletes documents whose expiry passed for accounts that are Free *at deletion
  time* (re-checked inside the delete transaction), using the existing document removal and object cleanup
  paths, and writes an audit event.
- Creating a document when the account already has `FreeMaxDocuments` active documents returns `429`
  `plan_limit_reached` (`kind: documents`).

### 2.4 Plan settings

One settings row (versioned by `UpdatedAt`) holds: `Phase`, `EnforceFromUtc`, `FreeOcrPagesPerDay`,
`FreeWatermarkExportsPerDay`, `FreeMaxDocuments`, `FreeRetentionDays`,
`UsageTimeZone`. Initial values come from configuration section `Plans` (seed only). Edits apply on the
next request (cached ≤ 30 s). Validation: non-negative integers within sane bounds (OCR ≤ 10 000/day,
documents ≤ 100 000, retention 1–3650 days, a valid IANA time zone). Every change is audited with old →
new values.

## 3. Data model (new tables)

| Table | Columns (key ones) | Notes |
|---|---|---|
| `accounts` | `FirebaseUid` PK, `Email` null, `SignInProvider`, `IsGuest`, `CreatedAt`, `LastSeenAt`, `RetentionGraceFrom` null | Upserted by authentication middleware at most once per account per 10 minutes; backfilled from `documents.OwnerFirebaseUid` by migration. |
| `subscriptions` | `Id`, `AccountUid`, `Source` (Manual\|HitPay\|AppStore\|GooglePlay), `Status` (Active\|Revoked\|Ended), `StartsAt`, `EndsAt` null, `Note`, `GrantedByUid` null, `CreatedAt`, `RevokedAt` null, `RevokedByUid` null | History kept; Pro = any active, in-window row. |
| `admins` | `AccountUid` PK, `AddedByUid` null, `AddedAt` | Owner seeded from config `Admin:OwnerEmail` on startup if the table is empty and that account exists (or when it first signs in). |
| `plan_settings` | single row: fields in 2.4, `UpdatedAt`, `UpdatedByUid` | |
| `usage_days` | `AccountUid`, `Day` (date), `OcrPages`, `WatermarkExports`, `BonusOcrPages` — PK (`AccountUid`,`Day`) | Created only when something is counted. |
| `payments` | `Id`, `AccountUid`, `Provider`, `ProviderReference`, `Amount`, `Currency`, `Status`, `CreatedAt`, `SubscriptionId` null | Empty until HitPay; read-only in this piece. |

The application database role receives the needed grants (Grant-ApplicationPermissions.ps1 is updated).

## 4. Admin portal

Route `/admin` in the existing Angular app. An **Admin** link appears in the top bar only for admins;
non-admins get the normal not-found page. The API re-checks admin rights on every request
(`Admin` authorization policy: signed-in, not a guest, account present in `admins`).

Pages (left menu):

1. **Dashboard** — total users (signed-in vs guests), new users today/7/30 days, active users 7 days,
   Pro subscribers (manual vs paid), documents created, OCR pages today/month with estimated Google cost
   (`OcrCostPerThousandPages`, default 1.50 USD, editable), watermarked exports; 30-day chart of new users
   and OCR pages; current phase banner ("Test phase — switches to Enforced on …").
2. **Users** — search by email or UID; columns: email, plan (+source), sign-in method, joined, last active,
   documents, today's usage. Detail: Pro history, usage last 30 days, actions **Give Pro** (1 month /
   3 months / 1 year / forever + note) and **Revoke Pro**.
3. **Subscribers** — active Pro: source, start, end/forever, note, granted by; **Extend** and **Revoke**.
4. **Payments** — read-only table; empty state explains it fills once HitPay is connected.
5. **Settings** — phase, auto-switch date, all numbers in 2.4 plus OCR cost; save with confirmation
   ("Apply to all users now?").
6. **Admins** — list; **Add admin** by email (account must exist); **Remove** (blocked for the last admin
   and for removing yourself when you are the last).

API: `GET/PUT /api/admin/settings`, `GET /api/admin/dashboard`, `GET /api/admin/users?query=&page=`,
`GET /api/admin/users/{uid}`, `POST /api/admin/users/{uid}/subscriptions`,
`POST /api/admin/subscriptions/{id}/revoke`, `POST /api/admin/subscriptions/{id}/extend`,
`GET /api/admin/subscriptions`, `GET /api/admin/payments`, `GET/POST /api/admin/admins`,
`DELETE /api/admin/admins/{uid}`, `GET /api/admin/audit?limit=`. Paginated (50 per page).
All mutating calls write audit events (`admin.pro_granted`, `admin.pro_revoked`, `admin.pro_extended`,
`admin.admin_added`, `admin.admin_removed`, `admin.settings_changed`).

User-facing API: `GET /api/me/plan` → plan, phase, pro-until, limits and today's usage, document
expiry rules.

## 5. User-facing changes

- **Edit tab**: "OCR today: 3 / 5" near Recognize text; at the limit Recognize text and Search's Recognize
  show "You've used today's 5 free OCR pages — upgrade to Pro or come back tomorrow."
- **Export tab**: "Watermarks today: 1 / 3"; at the limit the watermark switch shows the upgrade note and
  plain export still works. Pro: no brand stamp (builder reads the export's snapshot flag).
- **Documents**: Free/Enforced cards show "Deletes in N days"; banner the day before; creating beyond the
  maximum shows the upgrade message.
- **Account menu**: "Free" or "Pro until <date> / Pro (forever)" and an **Upgrade** button that reads
  "Pro is coming soon" until HitPay exists.
- **Test phase**: usage lines show; no limit messages, no deletions, stamp stays.

The brand-stamp decision is taken when the export is created and stored in the export snapshot, so a
PDF built later reflects the plan at request time.

## 6. Safety

- Limits enforced server-side; the UI only mirrors `GET /api/me/plan`.
- Atomic counters (2.2); deletion re-checks plan inside its transaction.
- Admin endpoints: Firebase identity + App Check + `Admin` policy; guests never admins; last-admin guard.
- Audit events for all admin actions and settings changes, using the existing tamper-evident audit chain.
- No document content, emails in logs; admin lists show email only to admins.

## 7. Testing

- Domain/application: entitlements per phase and plan, Pro window edges, day rollover in
  `Asia/Kuala_Lumpur`, bonus pages, settings validation.
- Persistence/integration: concurrent consumption never exceeds the limit; retention job deletes only
  expired Free documents and skips accounts that became Pro; grace on Pro end; last-admin guard;
  account upsert and backfill.
- API: admin policy (non-admin 404/403, guest refused), each admin endpoint, `plan_limit_reached` shape,
  `/api/me/plan`.
- Web: usage lines, limit messages, account menu, each admin page, admin-only link.
- Manual: full run on the local stack.

## 8. Out of scope

HitPay checkout and webhooks; mobile ads; App Store / Google Play billing; email notifications;
proration, coupons, invoices.
