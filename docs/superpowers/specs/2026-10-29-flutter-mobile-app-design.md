# Arks Scanner mobile app (Flutter) — design

Date: 2026-10-29. Status: draft for owner review.

## 1. Intent

Arks Scanner is a working Angular web app backed by an ASP.NET Core API, a background Worker, PostgreSQL, Cloudflare R2 and Firebase. Phone users need a real app: the web app has file upload only, no live camera scanning, and its editor is laid out for a desktop.

The owner chose a **Flutter** app for the native feel, accepting that the screens are rebuilt (the web UI is ~12,800 lines across 38 Angular components) and that future features are built twice (web and mobile).

The main job on a phone is: **scan → auto-crop → edit with the tools → export or download.** It should feel as quick as CamScanner without its interruptions.

The owner also wants to **keep cloud storage cost low**, because most users will be Free. The agreed model: Free documents live on the phone and the server copy is temporary; Pro keeps documents in the cloud and syncs with the web.

## 2. Goals

1. Android app on Google Play first; iOS from the same code later (no rewrite).
2. Native document scanner: live edge outline, auto-capture, multi-page batch, on-device crop.
3. Feature parity with the web editor for the core flow (list in section 5), laid out for one-handed phone use.
4. Same account as the website (Firebase UID); same plans and daily limits.
5. Free users: finished documents stored on the phone; the server copy is deleted soon after.
6. Pro users: documents synced to the cloud and visible on the website, as today.
7. English, Malay, Chinese (Simplified) and Arabic (right-to-left), matching the web.

## 3. Non-goals (MVP)

- Push notifications, in-app purchase, AdMob rewarded ads and app lock (v1.1).
- Offline editing, deep links and home-screen quick actions (later).
- Guest use: **sign-in is required before scanning** (owner decision).
- Admin portal (stays web-only).
- On-device image processing beyond what the native scanner does. Magic Scan, looks, eraser, OCR, text replacement and PDF building stay on the server.
- Changes to the web app beyond the Free retention setting in section 8.

## 4. Anti-goals (a failure even if it technically works)

- Ads, upgrade pop-ups or tips interrupting the scan flow. Upgrade prompts appear only when a limit is actually hit.
- A large watermark on Free exports (the existing small "Scanned with Arks Scanner" stamp is the only branding).
- Making users wait for every page before they can do anything. Pages appear one by one as the server finishes them.
- Leaving private document copies behind: on the phone after sign-out, or on the server for Free users beyond the short retention.
- Extra tracking or analytics SDKs beyond Firebase.
- Results that differ from the web for the same edit. The server is the only renderer.

## 5. Features

### 5.1 Sign-in and first launch
- Welcome screen, then **Sign in with Google** or **email/password** (create account or sign in). **Sign in with Apple** is added for the iOS release.
- No guest mode. After sign-in, the app opens straight to **My documents**.
- One-time privacy consent after the first sign-in (`POST /api/me/consent`), as on the web.
- Settings: language, sign out, **download my data** (`GET /api/me/export`), **delete account** (`DELETE /api/me`, required by both stores), plan and usage (`GET /api/me/plan`), licences.
- **Firebase App Check** with Play Integrity (Android) and App Attest (iOS). Every API call sends the Firebase ID token and App Check token, exactly as the web does.

### 5.2 Home and My documents
- Large **Scan** button plus **Import from photos / files**.
- Document list with first-page thumbnail, name and date. Smart default name such as "Scan 29 Oct 2026 15:40"; quick rename.
- Free: lists documents saved on this phone. Pro: lists cloud documents (as on web) plus any saved only on this phone.
- Delete, and for Pro the recycle bin and restore (`/api/documents/bin`, `/restore`).

### 5.3 Scan
- The native document scanner (Google ML Kit Document Scanner on Android, Apple VisionKit on iOS) with live edges, auto-capture, batch capture, and the scanner's own crop/rotate review.
- Import from gallery or files: JPEG, PNG, HEIC, PDF. Same limits as the server: 25 MB per file, 50 pages per import and per document.
- Pages upload through the existing upload flow (`POST /api/documents/{id}/uploads` → pre-signed R2 PUT → server validation, virus scan and page expansion).

### 5.4 Auto-crop and looks
- The server crops and straightens each page and applies **Magic scan** by default, the same as the web.
- A **filter strip** of previews under the page (Magic scan, Original, Document, Bright, No shadow, Grayscale, Black & white): tap to apply to this page; "Apply to all".
- **Adjust corners** (large touch handles, magnifier while dragging), **rotate** and **auto-detect again** (`.../crop` endpoints).
- Pages appear as they finish; the user can open page 1 while later pages are still processing.

### 5.5 Page grid
- Thumbnail grid: tap to open, long-press and drag to reorder (`PUT /page-order`), delete, **+** to add pages (scan or import).

### 5.6 Edit tools (bottom tool bar)
Crop · Looks · Eraser · Text · Sign · Tick · Export. Settings open in a bottom sheet, never a side panel.
- **Eraser:** paint over punch holes, staples or debris with a finger; preview, then apply (`.../repair`).
- **Recognize text / search:** run OCR on a page (`.../ocr`), search recognised words in the document, highlight matches.
- **Replace and add text** (`.../text-edits`):
  - Pinch-zoom, then tap a word to select it, with a magnifier while selecting.
  - The server's style proposal (`style-proposal`) pre-fills font, size, weight and colour, so most edits are: type the new word, then Apply.
  - The bottom sheet shows the text box and main options; **More** opens font (42 families, 14 handwriting), weight, colour, letter spacing, alignment and strikethrough.
  - A live preview strip above the keyboard shows the word while typing. The live preview thickens weights above the real face exactly as the server does (2 × 0.009 em per 100).
  - **Preview exact result** renders on the server before Apply. Undo and redo.
- **Signature:** draw with a finger or import a photo; the server removes the background (`POST /api/signatures/prepare`); place, resize and move.
- **Tick / cross marks:** tap a box to place; change colour, size and stroke.

### 5.7 Export, share, download
- PDF (optionally searchable), watermark options, or original file (`/exports`).
- **Share** to any app (WhatsApp, email, Drive…), **Save to Downloads / Files**, **Print** (system print service).
- Daily limits come from the server; when a limit is reached, show the server's message and the upgrade path. No other prompts.

### 5.8 Languages
- English, Malay, Chinese (Simplified), Arabic with right-to-left layout. Strings come from the web dictionaries (`apps/web/src/app/core/i18n/lang/*.ts`, ~980 keys each), converted to Flutter's ARB format. Keys stay in sync between web and mobile (section 9).

## 6. Storage model

| | Free | Pro |
|---|---|---|
| Where the finished document lives | **On the phone** (app storage; exported PDF and final page images) | **Cloud** (as web today), synced with the website; can also be saved to the phone |
| Server copy | Temporary: exists only while editing. When the user taps **Done / Save to phone**, the app downloads the results and deletes the server copy. Anything left over is removed by the Free retention period. | Kept while the plan is active, as today |
| Visible on the website | No | Yes |
| Editing a saved document again | Re-uploads the saved page images as a new server document (looks and edit history from the earlier session are not kept) | Opens the cloud document directly |
| After sign-out | Phone copies are **removed** from app storage (anti-goal: nothing private left behind). Before signing out, warn the user that phone-only documents will be deleted and offer to export them first. | Cloud copies remain; local copies removed |

Notes:
- Phone documents are stored in the app's private storage, not the shared gallery. Exports the user shares or saves to Downloads are, of course, the user's own files.
- If Pro ends, the account follows the existing Free rules (retention grace from `RetentionGraceFrom`); the app offers to save cloud documents to the phone.

## 7. Architecture

- **Code:** new Flutter project at `apps/mobile/` in this repository.
- **Backend:** the existing API is used as-is, apart from the items in section 8. All rendering and image processing stays on the server, so phone and web produce identical results.
- **Auth:** Firebase Auth (`firebase_auth`), Google sign-in, email/password; ID token on every request. App Check (`firebase_app_check`) with Play Integrity / App Attest.
- **Uploads:** pre-signed R2 URLs from the existing upload intent endpoint; the phone PUTs bytes directly to R2.
- **Fonts:** the editor needs the catalogue's faces for its live preview. Latin and handwriting faces are bundled in the app; large CJK, Arabic and other world-script faces are downloaded on demand from `GET /api/fonts/{file}` and cached. The server manifest (`GET /api/text-edit-fonts`) is the source of truth.
- **Local store:** a small local database indexes phone-only documents (name, dates, page files, exported PDF).
- **Package choices** are left to the builder, with these requirements: a maintained scanner plugin covering ML Kit (Android) and VisionKit (iOS); share, print and file-save support; ARB localisation with RTL.

## 8. Backend and configuration changes

1. **Firebase:**
   - Register the Android app (package name to be chosen) in the Firebase project and enable Play Integrity for App Check.
   - Add the Android app ID to `Firebase__AllowedAppIds`.
   - Later, do the same for iOS and add Sign in with Apple.
2. **Fast removal of phone-saved documents:** add a removal reason (e.g. `saved_to_device`) that the existing purge job erases on its next run, instead of keeping it in the 30-day recycle bin. Mobile **Done / Save to phone** deletes with this reason.
3. **Free retention (web and mobile):** set `FreeRetentionDays` in the admin portal to a short period (owner's choice: 1 or 7 days) and set `EnforceFromUtc` when ready. No code change. The web should tell Free users to download before documents expire; the existing plan messages may already cover this, and the builder should check.
4. Nothing else on the server is required for the MVP. v1.1 adds:
   - purchase receipt checks;
   - a device push-token table, with the Worker sending notifications through FCM;
   - an ad-reward endpoint crediting `BonusOcrPages`.

## 9. Constraints

- Android first; built and tested on this Windows PC with Android Studio or the Android SDK. iOS builds need a Mac or a cloud Mac build service.
- Store accounts:
  - Google Play Console: an **organization** account avoids the 12-tester / 14-day closed test required for new personal accounts.
  - Apple Developer account for iOS.
- Server limits and validation are unchanged; the phone must handle the same error codes the web handles (e.g. `plan_limit_reached`, `ocr_unsupported_media`, `crop_failed`).
- The four language dictionaries must keep the same keys and placeholders (the web enforces this in `i18n.service.spec.ts`; mobile needs an equivalent check).
- Privacy: no document content, file names, OCR text or tokens in logs or analytics.

## 10. Milestones (each ends with something installable on a phone)

1. **Skeleton:** Flutter project, sign-in, App Check, consent, My documents (read-only list).
2. **Scan & import:** native scanner, gallery/file import, upload, live page processing.
3. **Crop, looks, page grid:** filter strip, corners, rotate, reorder, delete, add pages.
4. **Export & phone storage:** PDF/original export, share, save, print; the Free phone-storage model, including the `saved_to_device` backend change.
5. **Edit tools 1:** eraser, signatures, tick/cross.
6. **Edit tools 2:** OCR, search, text replacement and add text.
7. **Languages & polish:** ms, zh, ar (RTL), settings, delete account, store listing, closed testing.

## 11. Decisions made by the assistant (owner may override)

- Android first, because the PC is Windows; iOS follows from the same code.
- App lives in `apps/mobile/` in this repository.
- Free = phone storage with a temporary server copy; Pro = cloud sync. (The owner agreed to this storage model in principle.)
- On sign-out, phone-only documents are removed after a warning.
- Editing a phone-saved Free document again re-imports it as a new server document.
- New `saved_to_device` removal reason for fast purge.
- Latin and handwriting fonts bundled; world-script fonts downloaded on demand.

## 12. Left to the builder

- Flutter state management, navigation and HTTP libraries, and the local database.
- Exact phone layouts within the rules above (bottom tool bar, bottom sheets, large touch targets).
- Thumbnail and image caching strategy.
- Android package name and app icon from `docs/brand`.
