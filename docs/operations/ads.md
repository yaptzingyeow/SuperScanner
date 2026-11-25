# Ads (Free accounts only)

Pro accounts never see ads. Everything is **off** until switched on.

| Where | Ad | When it shows |
|---|---|---|
| Mobile app | AdMob **rewarded** video | Only after a Free account hits its daily OCR limit, and only if the person taps **Watch ad**. The reward is extra OCR pages for that day. |
| Website | AdSense **anchor** bar (bottom, closable) | Free accounts, **upload page only** |

## Privacy rules built in

- **The website ad script never runs next to documents.** AdSense can read the page it is on, and a loaded script keeps running in a single-page app. So it loads only on the upload page, which shows no document content. Opening anything else first does a **full page reload**, which removes the script (`apps/web/src/app/ads/web-ads.service.ts`).
- **The app sends no document content to ad networks.** The ad SDK starts only when someone chooses to watch an ad.
  - Google's consent form appears where the law requires it (EEA/UK).
  - On iPhone, Apple's tracking prompt appears. Refusing it still allows non-personalised ads.
- **The privacy notice is version `2026-11`.** It mentions ads, so every user accepts it once more.

## Rewards can't be faked

1. The app asks the API for a one-time reward id (`POST /api/me/ad-rewards`).
2. The app shows the ad with that id as AdMob **server-side verification** custom data, and the Firebase UID as user id.
3. After the view, **Google** calls `GET /api/ads/admob/verify` with a signed query string. The API checks the ECDSA signature against Google's published keys. It then grants the reward once: transaction ids are unique and rewards expire after 1 hour. The pages are added to that day's `BonusOcrPages`.
4. The app polls `GET /api/me/ad-rewards/{id}` and retries the OCR request.

**Google's test ad units never call step 3.** For testing only, `Ads__AllowUnverifiedRewards=true` lets the app claim its own reward (`POST /api/me/ad-rewards/{id}/claim`). The API logs a warning at start-up while this is on. **Turn it off before real ads go live**, because a modified app could otherwise claim rewards without watching.

## API switches (Railway → api → Variables)

| Variable | Default | Meaning |
|---|---|---|
| `Ads__RewardedEnabled` | `false` | Offer rewarded ads in the app |
| `Ads__RewardedOcrPages` | `5` | Extra OCR pages per ad (1–100) |
| `Ads__MaxRewardsPerDay` | `3` | Ads per account per day (0–50) |
| `Ads__AllowUnverifiedRewards` | `false` | **Test only**, see above |
| `Ads__WebAnchorEnabled` | `false` | Website anchor bar |
| `Ads__AdSenseClientId` | — | `ca-pub-…` publisher id (required when the anchor is on) |

Rewarded ads only matter once the plan phase is **Enforced**. In the Test phase OCR is unlimited, so nobody reaches the limit.

**Database:** this release adds the `ad_rewards` table. Run `.\tools\railway\migrate.ps1` after the API is deployed.

## Mobile setup (AdMob)

1. Create an AdMob account. Add the **Android** app (`com.arksoft.arksscanner`) and the **iOS** app (same bundle id).
2. Create a **Rewarded** ad unit for each.
3. On each ad unit, enable **server-side verification** with callback URL `https://<api domain>/api/ads/admob/verify`, e.g. `https://api-production-6cbe.up.railway.app/api/ads/admob/verify`.
4. Builds use Google's **test** ids until you pass the real ones:
   - **Android:** `flutter build apk --dart-define=ADMOB_REWARDED_ANDROID=ca-app-pub-…/… -PadmobAppId=ca-app-pub-…~…`
   - **iOS (Codemagic group `ios_test`):** `ADMOB_APP_ID_IOS=ca-app-pub-…~…` and `ADMOB_REWARDED_IOS=ca-app-pub-…/…`
5. Real ads serve fully once the app is published on Google Play / the App Store and linked in AdMob.
6. In AdMob → **Privacy & messaging**, create the GDPR consent message, which the app shows automatically, and the iOS IDFA explainer if you want one.

## Website setup (AdSense)

1. **Domain:**
   - The website must be on your own domain. Add `arksscanner.arksoft.dev` as a custom domain of the Railway **web** service, and add the CNAME record at your DNS provider.
   - Add the domain to Firebase **Authorized domains** and to the website's reCAPTCHA (App Check) key.
2. **Apply:** apply for AdSense with the site **`arksoft.dev`**. Approval covers its subdomains. It needs a real site with content, an About page and a privacy policy.
3. **ads.txt:** put `ads.txt` at **`https://arksoft.dev/ads.txt`** (the root domain) with the line AdSense gives you, e.g. `google.com, pub-XXXXXXXXXXXXXXXX, DIRECT, f08c47fec0942fa0`.
4. **Consent:** in AdSense → **Privacy & messaging**, publish the European regulations (GDPR) message. Google then shows it to EEA/UK visitors.
5. **Allow Google's ad domains** in the web service's Content Security Policy. Set these on **Railway → web → Variables**; they are empty by default, which blocks ads:

   | Variable | Value |
   |---|---|
   | `ADS_SCRIPT_SRC` | `https://pagead2.googlesyndication.com https://*.googlesyndication.com https://*.google.com https://*.googleadservices.com https://*.doubleclick.net https://fundingchoicesmessages.google.com https://*.adtrafficquality.google` |
   | `ADS_FRAME_SRC` | `https://*.googlesyndication.com https://*.doubleclick.net https://*.google.com https://fundingchoicesmessages.google.com https://*.adtrafficquality.google` |
   | `ADS_CONNECT_SRC` | `https://*.google.com https://*.googlesyndication.com https://*.doubleclick.net https://fundingchoicesmessages.google.com https://*.adtrafficquality.google` |
   | `ADS_IMG_SRC` | `https:` |

6. **Turn it on:** on the **api** service, set `Ads__AdSenseClientId=ca-pub-…` and `Ads__WebAnchorEnabled=true`.
