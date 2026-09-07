[CmdletBinding()]
param(
    [string]$HostName = '127.0.0.1',
    [ValidateRange(1, 65535)][int]$Port = 5432,
    [string]$DatabaseName = 'SuperScannerDB',
    [string]$AdminUser = 'postgres',
    [Security.SecureString]$AdminPassword
)

. "$PSScriptRoot\Database.Common.ps1"
Assert-SafePostgresIdentifier $DatabaseName 'DatabaseName'
if (-not $AdminPassword) { $AdminPassword = Read-RequiredSecureString 'PostgreSQL administrator password' }

$repositoryRoot = (Resolve-Path -LiteralPath "$PSScriptRoot\..\..").Path
$previousConnection = $env:ConnectionStrings__PostgreSql
$previousEnvironment = $env:ASPNETCORE_ENVIRONMENT

try {
    Use-PlainText $AdminPassword {
        param($plainPassword)
        $env:ConnectionStrings__PostgreSql = New-NpgsqlConnectionString `
            -HostName $HostName -Port $Port -Database $DatabaseName `
            -UserName $AdminUser -Password $plainPassword
        $env:ASPNETCORE_ENVIRONMENT = 'Development'

        Push-Location $repositoryRoot
        try {
            & dotnet tool restore
            if ($LASTEXITCODE -ne 0) { throw 'dotnet tool restore failed.' }
            & dotnet ef database update `
                --project src/SuperScanner.Infrastructure/SuperScanner.Infrastructure.csproj `
                --startup-project src/SuperScanner.Infrastructure/SuperScanner.Infrastructure.csproj
            if ($LASTEXITCODE -ne 0) { throw 'Entity Framework migration failed.' }
        }
        finally {
            Pop-Location
        }
    }
}
finally {
    if ($null -eq $previousConnection) {
        Remove-Item Env:ConnectionStrings__PostgreSql -ErrorAction SilentlyContinue
    } else {
        $env:ConnectionStrings__PostgreSql = $previousConnection
    }
    if ($null -eq $previousEnvironment) {
        Remove-Item Env:ASPNETCORE_ENVIRONMENT -ErrorAction SilentlyContinue
    } else {
        $env:ASPNETCORE_ENVIRONMENT = $previousEnvironment
    }
}

Write-Host "Applied Entity Framework migrations to $DatabaseName on ${HostName}:$Port."
