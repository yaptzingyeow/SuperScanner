# SuperScanner secure foundation runbook

## Topology

Use separate Railway projects for production and non-production. Only `web` and `api` receive public domains. `worker` and PostgreSQL are private services and must never have a Railway-generated or custom public domain. The worker reaches PostgreSQL, R2, and ClamAV over outbound/private connections; the web service reaches the API through `API_UPSTREAM`.

| Service | Exposure | Health path | Container |
| --- | --- | --- | --- |
| Web | Public | `/health` | `apps/web/Dockerfile` |
| API | Public | `/health` | `src/SuperScanner.Api/Dockerfile` |
| Worker | Private only | `/health` | `src/SuperScanner.Worker/Dockerfile` |
| PostgreSQL | Private only | Railway TCP health check | Railway PostgreSQL |

Never reuse Firebase projects, R2 buckets, database credentials, audit keys, or provider accounts between environments.

## Local development and E2E

Docker Desktop must be running. The Compose defaults are deliberately local-only and must not be copied to a shared environment.

```powershell
docker compose up -d --wait
dotnet run --project src/SuperScanner.Api
dotnet run --project src/SuperScanner.Worker
npm --prefix apps/web start
```

For the integrated gate, keep the dependencies running and execute `npm --prefix apps/web run e2e`. The E2E identity route exists only when `ASPNETCORE_ENVIRONMENT=E2E`; startup fails if `E2E__IdentityFixtureEnabled` is supplied in Development, Staging, or Production. Rotate or remove every Compose default before any shared deployment.

## Railway variables

API requires `ConnectionStrings__PostgreSql`, `R2__AccountId`, `R2__AccessKeyId`, `R2__SecretAccessKey`, `R2__BucketName`, `Audit__SigningKeyBase64`, `Audit__SigningKeyId`, `Firebase__ProjectId`, `Firebase__ProjectNumber`, and each allowed Firebase app ID under `Firebase__AllowedAppIds__N`.

Worker requires the same database, R2, and audit variables plus `ClamAv__Host`, `ClamAv__Port`, and `ClamAv__OperationTimeout`. Web requires `API_UPSTREAM` and `R2_CONNECT_SOURCE`, which limits CSP uploads to the exact R2 or custom upload origin. Firebase's public browser configuration is supplied at build/deployment time and is not a server secret. Never configure an `E2E__*` variable outside a disposable E2E process.

Use at least 32 random bytes for the audit signing key, Base64 encoded. Store secrets only in Railway/Firebase/Cloudflare secret stores, never repository files or image build arguments.

## Deploy and migrate

1. Back up PostgreSQL and confirm the target environment and R2 bucket.
2. Deploy the API image with no public traffic and run `dotnet ef database update --project src/SuperScanner.Infrastructure --startup-project src/SuperScanner.Api` as a one-off Railway command.
3. Start the API and verify `/health`, then start the private worker and verify `/health` from Railway's private network.
4. Deploy web with `API_UPSTREAM` pointing to the API private hostname. Verify authentication, App Check, upload, validation, and owner isolation before promoting traffic.

Do not run automatic production migrations concurrently from multiple API replicas.

## Incident controls

- **Pause processing:** scale the private worker to zero. Uploads remain quarantined and pending; the API can continue accepting requests only if the incident owner permits it.
- **Resume processing:** correct the dependency or credentials, deploy one worker, confirm leases and validation outcomes, then restore normal replicas.
- **Inspect quarantine:** use R2 inventory and object metadata by opaque object key. Do not download document bytes to staff devices or log filenames, signed URLs, hashes, OCR text, or tokens.
- **Disable sharing/providers:** set the relevant share/provider feature switch to disabled and restart API/worker. Phase 1 ships these capabilities disabled; keep the switches off until their dedicated security gate passes.
- **Suspected malware:** pause workers, preserve audit/database evidence, isolate affected quarantine keys, rotate R2 credentials, and follow the security incident process. Never promote or manually copy quarantined content.

## Secret rotation

Create a replacement credential first, update the consuming Railway services, verify health and a synthetic upload, then revoke the old credential. For the audit signing key, assign a new `Audit__SigningKeyId` and retain old verification material according to the audit-retention policy. Rotate Firebase service credentials, App Check configuration, R2 keys, PostgreSQL credentials, and ClamAV access independently so rollback remains possible.

## Rollback

Stop the worker before rollback if the release changes job or persistence behavior. Roll web, API, and worker back to the last known image digest; do not reverse a database migration until a reviewed down-migration and backup restore have been tested. Verify `/health`, Firebase plus App Check rejection/acceptance, owner isolation, quarantine validation, idempotent retry, and audit-chain verification before restoring traffic.
