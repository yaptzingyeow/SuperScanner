[CmdletBinding()]
param(
    [string]$HostName = '127.0.0.1',
    [ValidateRange(1, 65535)][int]$Port = 5432,
    [string]$AdminUser = 'postgres',
    [Security.SecureString]$AdminPassword
)

# One-off rebrand: SuperScannerDB -> ArksScannerDB and superscanner_app -> arksscanner_app.
# Safe to re-run: each step only happens while the old name still exists.
# Stop the API and Worker first; open connections to the database are closed.

. "$PSScriptRoot\Database.Common.ps1"
if (-not $AdminPassword) { $AdminPassword = Read-RequiredSecureString 'PostgreSQL administrator password' }
$psql = Find-Psql

$sql = @'
DO $$
DECLARE
    password_kept boolean;
BEGIN
    IF EXISTS (SELECT 1 FROM pg_database WHERE datname = 'SuperScannerDB')
       AND NOT EXISTS (SELECT 1 FROM pg_database WHERE datname = 'ArksScannerDB') THEN
        PERFORM pg_terminate_backend(pid) FROM pg_stat_activity
            WHERE datname = 'SuperScannerDB' AND pid <> pg_backend_pid();
        EXECUTE 'ALTER DATABASE "SuperScannerDB" RENAME TO "ArksScannerDB"';
        RAISE NOTICE 'Database renamed to ArksScannerDB.';
    ELSE
        RAISE NOTICE 'Database rename skipped (already done or SuperScannerDB not found).';
    END IF;

    IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'superscanner_app')
       AND NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'arksscanner_app') THEN
        -- SCRAM passwords survive a rename; MD5 passwords are salted with the name and are cleared.
        SELECT rolpassword LIKE 'SCRAM-SHA-256$%' INTO password_kept FROM pg_authid WHERE rolname = 'superscanner_app';
        EXECUTE 'ALTER ROLE superscanner_app RENAME TO arksscanner_app';
        IF password_kept THEN
            RAISE NOTICE 'Role renamed to arksscanner_app; its password is unchanged.';
        ELSE
            RAISE WARNING 'Role renamed to arksscanner_app, but its MD5 password was cleared. Set it again: ALTER ROLE arksscanner_app PASSWORD ''...'';';
        END IF;
    ELSE
        RAISE NOTICE 'Role rename skipped (already done or superscanner_app not found).';
    END IF;
END $$;
SELECT 'database: ' || string_agg(datname, ', ') FROM pg_database WHERE datname IN ('SuperScannerDB', 'ArksScannerDB');
SELECT 'role: ' || string_agg(rolname, ', ') FROM pg_roles WHERE rolname IN ('superscanner_app', 'arksscanner_app');
'@

Use-PlainText $AdminPassword {
    param($plainPassword)
    Invoke-Psql -PsqlPath $psql -HostName $HostName -Port $Port -Database 'postgres' `
        -UserName $AdminUser -Password $plainPassword -Sql $sql | ForEach-Object { Write-Host $_ }
}
Write-Host 'Done. Start the API and Worker again with the .task-tools start scripts.'
