[CmdletBinding()]
param(
    [string]$HostName = '127.0.0.1',
    [ValidateRange(1, 65535)][int]$Port = 5432,
    [string]$DatabaseName = 'SuperScannerDB',
    [string]$AdminUser = 'postgres',
    [string]$ApplicationRole = 'superscanner_app',
    [Security.SecureString]$AdminPassword,
    [Security.SecureString]$ApplicationPassword
)

. "$PSScriptRoot\Database.Common.ps1"
Assert-SafePostgresIdentifier $DatabaseName 'DatabaseName'
Assert-SafePostgresIdentifier $ApplicationRole 'ApplicationRole'

if (-not $AdminPassword) { $AdminPassword = Read-RequiredSecureString 'PostgreSQL administrator password' }
if (-not $ApplicationPassword) { $ApplicationPassword = Read-RequiredSecureString "$ApplicationRole password" }
$psql = Find-Psql

Use-PlainText $AdminPassword {
    param($plainAdminPassword)
    Use-PlainText $ApplicationPassword {
        param($plainApplicationPassword)
        $escapedPassword = $plainApplicationPassword.Replace("'", "''")
        $roleSql = @"
DO `$setup`$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = '$ApplicationRole') THEN
        CREATE ROLE "$ApplicationRole" LOGIN PASSWORD '$escapedPassword' NOSUPERUSER NOCREATEDB NOCREATEROLE NOREPLICATION;
    ELSE
        ALTER ROLE "$ApplicationRole" WITH LOGIN PASSWORD '$escapedPassword' NOSUPERUSER NOCREATEDB NOCREATEROLE NOREPLICATION;
    END IF;
END
`$setup`$;
"@
        Invoke-Psql -PsqlPath $psql -HostName $HostName -Port $Port -Database postgres `
            -UserName $AdminUser -Password $plainAdminPassword -Sql $roleSql | Out-Null

        $existsSql = "SELECT 1 FROM pg_database WHERE datname = '$DatabaseName';"
        $databaseExists = Invoke-Psql -PsqlPath $psql -HostName $HostName -Port $Port -Database postgres `
            -UserName $AdminUser -Password $plainAdminPassword -Sql $existsSql
        if (-not ($databaseExists -contains '1')) {
            Invoke-Psql -PsqlPath $psql -HostName $HostName -Port $Port -Database postgres `
                -UserName $AdminUser -Password $plainAdminPassword -Sql "CREATE DATABASE `"$DatabaseName`";" | Out-Null
            Write-Host "Created database $DatabaseName."
        } else {
            Write-Host "Database $DatabaseName already exists."
        }
    }
}

& "$PSScriptRoot\Apply-DatabaseMigrations.ps1" -HostName $HostName -Port $Port `
    -DatabaseName $DatabaseName -AdminUser $AdminUser -AdminPassword $AdminPassword
& "$PSScriptRoot\Grant-ApplicationPermissions.ps1" -HostName $HostName -Port $Port `
    -DatabaseName $DatabaseName -AdminUser $AdminUser -ApplicationRole $ApplicationRole `
    -AdminPassword $AdminPassword
& "$PSScriptRoot\Test-DatabaseSetup.ps1" -HostName $HostName -Port $Port `
    -DatabaseName $DatabaseName -AdminUser $AdminUser -ApplicationRole $ApplicationRole `
    -AdminPassword $AdminPassword

Write-Host 'Local database setup completed. Passwords were not saved.'
