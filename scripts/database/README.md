# Local database scripts

These scripts initialize and verify the installed Windows PostgreSQL instance without storing credentials in the repository or command history.

## Prerequisites

- PostgreSQL is running and `psql.exe` is installed.
- .NET 10 SDK is installed.
- The PostgreSQL administrator password is available.

## Complete setup

From the repository root, run:

```powershell
& .\scripts\database\Setup-LocalDatabase.ps1
```

The defaults target `127.0.0.1:5432`, database `SuperScannerDB`, administrator `postgres`, and application role `superscanner_app`. The command securely prompts for the administrator password and the application-role password. It creates or updates the role, creates the database when absent, applies all Entity Framework migrations, grants least-privilege access, and verifies the result.

To override connection details:

```powershell
& .\scripts\database\Setup-LocalDatabase.ps1 `
  -HostName 127.0.0.1 `
  -Port 5432 `
  -DatabaseName SuperScannerDB `
  -AdminUser postgres `
  -ApplicationRole superscanner_app
```

## Run individual stages

```powershell
& .\scripts\database\Apply-DatabaseMigrations.ps1
& .\scripts\database\Grant-ApplicationPermissions.ps1
& .\scripts\database\Test-DatabaseSetup.ps1
```

Each command prompts for the administrator password. Passwords reach child processes only through temporary process-scoped environment variables or standard input and are restored or removed afterward.

## Runtime connection

Run the API and Worker using the restricted `superscanner_app` role, not the PostgreSQL administrator. Supply `ConnectionStrings__PostgreSql` through a process-scoped environment variable or a deployment secret store. Do not place the password in `appsettings.json`, `.env` files, Git, screenshots, or support messages.

The connection-string shape is:

```text
Host=127.0.0.1;Port=5432;Database=SuperScannerDB;Username=superscanner_app;Password=<prompted password>
```

## Password rotation

Running `Setup-LocalDatabase.ps1` again updates the `superscanner_app` password and safely reapplies migrations and grants. Update the API and Worker secret before restarting them.

## Recovery

The scripts never drop the database, tables, roles, or data. Before a production migration, take a PostgreSQL backup and follow `docs/operations/foundation-runbook.md`. Do not use automatic down-migrations as a rollback strategy.
