# Railway environment

Railway project **rare-alignment**, environment `production`, region US East, Hobby plan. It follows the [foundation runbook](foundation-runbook.md) topology.

| Service | Source | Public address |
|---|---|---|
| `api` | `src/ArksScanner.Api/Dockerfile` | `https://api-production-6cbe.up.railway.app`, port 8080 |
| `web` | `apps/web/Dockerfile` | `https://web-production-87111.up.railway.app` |
| `worker` | `src/ArksScanner.Worker/Dockerfile` | none (private) |
| `Postgres` | Railway PostgreSQL, with a volume | none (private) |

- The services deploy automatically from the GitHub branch shown in each service's Settings → Source. That's currently `codex/phase-1-foundation`.
- Railway's config-as-code files are deprecated, and services that never used them can't opt in. Dockerfile paths and health checks are set in each service's Settings instead.

## Mobile apps

The API's public domain exists so phones can reach it; the website still uses the private `api.railway.internal`.

The API only accepts Firebase apps listed in its variables:

| Variable | App |
|---|---|
| `Firebase__AllowedAppIds__0` | Web `1:122650027029:web:074aeaa1f2ac7a3d471a1c` |
| `Firebase__AllowedAppIds__1` | Android `1:122650027029:android:d0451230709c0d93471a1c` |
| `Firebase__AllowedAppIds__2` | iOS `1:122650027029:ios:399b7d20d20f1e26471a1c` |

Mobile builds use:
- `API_BASE_URL=https://api-production-6cbe.up.railway.app/api`
- `WEB_BASE_URL=https://web-production-87111.up.railway.app` (for the text editor's fonts)

## Migrations

The runtime images don't run migrations. After a release that adds migrations:
1. Enable **Postgres → Settings → Networking → TCP Proxy**.
2. Run `.\tools\railway\migrate.ps1` and paste `DATABASE_PUBLIC_URL` when asked.
3. Disable the proxy again.

## Checks

- `GET /health/ready` on the API returns `Healthy` once the database is reachable and migrated.
- `GET /api/documents` without sign-in returns `401`.
