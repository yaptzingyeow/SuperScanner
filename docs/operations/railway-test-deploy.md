# Railway test environment (API, Worker, PostgreSQL, R2)

A shared test server so phones can reach the API over https. It follows the [foundation runbook](foundation-runbook.md) topology:
- Only the API gets a public domain.
- The Worker and PostgreSQL stay private.

The website (`apps/web`) is optional here; the mobile apps only need the API.

**Test-only shortcut:** malware scanning (ClamAV) is off (`MalwareScanning__Enabled=false`). Production must run a private ClamAV service and remove that variable.

## 1. Cloudflare R2 (file storage)

1. In the Cloudflare dashboard, go to **R2 Object Storage**.
   - The first time, R2 asks for a payment card. The free tier is 10 GB with no egress fees.
2. **Create bucket** named `arksscanner-test`, with location **Asia-Pacific**.
3. Go to **R2 → Manage API tokens → Create API token**.
   - Permission: **Object Read & Write**.
   - Specify bucket: `arksscanner-test` only.
4. Keep the **Access Key ID**, the **Secret Access Key** and your **Account ID**. The Account ID is shown on the R2 overview page.

Uploads from the mobile apps don't need CORS. The website would need a CORS rule allowing `PUT` from its origin.

## 2. Railway project

1. Go to <https://railway.com>, choose **New Project → Deploy PostgreSQL**. Rename the database service to `Postgres`.
2. **+ Create → GitHub Repo → SuperScanner** for the API:
   - Settings → Source → Branch: `mobile-app`.
   - Settings → **Config-as-code → Railway config file**: `/railway/api.json`.
   - Settings → Networking → **Generate Domain**, port `8080`.
3. **+ Create → GitHub Repo → SuperScanner** again for the Worker:
   - Branch `mobile-app`, config file `/railway/worker.json`.
   - **No** domain.
4. Name the services `api` and `worker`.
5. For every service, set Settings → **Region → Southeast Asia (Singapore)**. A US region adds about 200 ms to each request from Malaysia.
6. For the API, keep **Serverless / app sleeping off**. Otherwise the first request after an idle period waits for the API to wake.

## 3. Variables

Use Railway's **Shared Variables** (project settings) for values both services use, then reference them from each service.

### Both API and Worker

| Variable | Value |
|---|---|
| `ConnectionStrings__PostgreSql` | `Host=${{Postgres.PGHOST}};Port=${{Postgres.PGPORT}};Database=${{Postgres.PGDATABASE}};Username=${{Postgres.PGUSER}};Password=${{Postgres.PGPASSWORD}}` |
| `R2__AccountId` | Cloudflare Account ID |
| `R2__AccessKeyId` | R2 token Access Key ID |
| `R2__SecretAccessKey` | R2 token Secret Access Key |
| `R2__BucketName` | `arksscanner-test` |
| `Audit__SigningKeyBase64` | 32 random bytes in Base64; see below |
| `Audit__SigningKeyId` | `railway-test-1` |
| `TextEditing__Enabled` | `true` |
| `Ocr__Enabled` | `true` |
| `Ocr__Provider` | `Tesseract` |
| `Ocr__Language` | `en` |

To make the audit key, run this in PowerShell and paste the output straight into Railway. Don't put it in any file or chat.

```powershell
[Convert]::ToBase64String([Security.Cryptography.RandomNumberGenerator]::GetBytes(32))
```

### API only

| Variable | Value |
|---|---|
| `Firebase__ProjectId` | `superscanner-dev` |
| `Firebase__ProjectNumber` | `122650027029` |
| `Firebase__AllowedAppIds__0` | `1:122650027029:web:074aeaa1f2ac7a3d471a1c` |
| `Firebase__AllowedAppIds__1` | `1:122650027029:android:d0451230709c0d93471a1c` |
| `Firebase__AllowedAppIds__2` | `1:122650027029:ios:399b7d20d20f1e26471a1c` |
| `Firebase__ServiceAccountJson` | The whole content of the Firebase Admin service account JSON file |

### Worker only

| Variable | Value |
|---|---|
| `MalwareScanning__Enabled` | `false` (test only) |

## 4. Create the database tables

The runtime images don't run migrations; the runbook says to run them as a separate step.
1. In Railway, open **Postgres → Settings → Networking** and enable the **TCP Proxy**.
2. Copy `DATABASE_PUBLIC_URL` from **Postgres → Variables**.
3. Run this from the repository on this PC, and paste the URL when asked:

```powershell
.\tools\railway\migrate.ps1
```

Run it again after every release that adds migrations. Disable the TCP proxy afterwards if you don't need it.

## 5. Check

- Open `https://<api domain>/health`. It should say `Healthy`.
- Open `https://<api domain>/health/ready`. It should say `Healthy` once the migrations have run.
- Worker → Deployments should show it as **Active**, with no restart loop.
- Point the mobile build at the API by setting Codemagic `API_BASE_URL` to `https://<api domain>/api`. Rebuild and reinstall.
