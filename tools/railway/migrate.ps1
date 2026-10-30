# Applies the EF Core migrations to a Railway PostgreSQL database from this PC (runbook: "Deploy and migrate").
# Paste the Postgres service's DATABASE_PUBLIC_URL when asked; it is read without echo and never written to disk.
# Requires the dotnet-ef tool: dotnet tool install --global dotnet-ef
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$repo = Resolve-Path (Join-Path $PSScriptRoot '..\..')

$secure = Read-Host 'DATABASE_PUBLIC_URL (postgresql://...)' -AsSecureString
$url = [Runtime.InteropServices.Marshal]::PtrToStringBSTR([Runtime.InteropServices.Marshal]::SecureStringToBSTR($secure))
if ($url -notmatch '^postgres(ql)?://') { throw 'Expected a postgresql:// URL.' }

$uri = [Uri]$url
$user, $password = $uri.UserInfo.Split(':', 2) | ForEach-Object { [Uri]::UnescapeDataString($_) }
$database = $uri.AbsolutePath.TrimStart('/')
$env:ConnectionStrings__PostgreSql =
    "Host=$($uri.Host);Port=$($uri.Port);Database=$database;Username=$user;Password=$password;SSL Mode=Prefer"

try {
    dotnet ef database update `
        --project (Join-Path $repo 'src/ArksScanner.Infrastructure') `
        --startup-project (Join-Path $repo 'src/ArksScanner.Infrastructure')
    if ($LASTEXITCODE -ne 0) { throw "dotnet ef exited with $LASTEXITCODE" }
    Write-Host 'Database is up to date.'
}
finally {
    Remove-Item Env:ConnectionStrings__PostgreSql -ErrorAction SilentlyContinue
}
