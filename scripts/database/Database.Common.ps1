Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Assert-SafePostgresIdentifier {
    param([Parameter(Mandatory)][string]$Value, [Parameter(Mandatory)][string]$Name)

    if ($Value -notmatch '^[A-Za-z_][A-Za-z0-9_]*$') {
        throw "$Name must start with a letter or underscore and contain only letters, numbers, and underscores."
    }
}

function Find-Psql {
    $command = Get-Command psql -ErrorAction SilentlyContinue
    if ($command) { return $command.Source }

    $installRoot = 'C:\Program Files\PostgreSQL'
    if (Test-Path -LiteralPath $installRoot) {
        $candidate = Get-ChildItem -LiteralPath $installRoot -Filter psql.exe -Recurse -ErrorAction SilentlyContinue |
            Where-Object { $_.FullName -match '\\bin\\psql\.exe$' } |
            Sort-Object FullName -Descending |
            Select-Object -First 1
        if ($candidate) { return $candidate.FullName }
    }

    throw 'psql was not found. Install PostgreSQL command-line tools or add its bin directory to PATH.'
}

function Read-RequiredSecureString {
    param([Parameter(Mandatory)][string]$Prompt)

    $value = Read-Host -Prompt $Prompt -AsSecureString
    if ($value.Length -eq 0) { throw "$Prompt cannot be empty." }
    return $value
}

function Use-PlainText {
    param(
        [Parameter(Mandatory)][Security.SecureString]$SecureValue,
        [Parameter(Mandatory)][scriptblock]$Action
    )

    $pointer = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($SecureValue)
    try {
        $plainText = [Runtime.InteropServices.Marshal]::PtrToStringBSTR($pointer)
        return & $Action $plainText
    }
    finally {
        [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($pointer)
        Remove-Variable plainText -ErrorAction SilentlyContinue
    }
}

function Invoke-Psql {
    param(
        [Parameter(Mandatory)][string]$PsqlPath,
        [Parameter(Mandatory)][string]$HostName,
        [Parameter(Mandatory)][int]$Port,
        [Parameter(Mandatory)][string]$Database,
        [Parameter(Mandatory)][string]$UserName,
        [Parameter(Mandatory)][string]$Password,
        [Parameter(Mandatory)][string]$Sql
    )

    $previousPassword = $env:PGPASSWORD
    try {
        $env:PGPASSWORD = $Password
        $output = $Sql | & $PsqlPath --no-password --no-psqlrc --set ON_ERROR_STOP=1 `
            --host $HostName --port $Port --username $UserName --dbname $Database `
            --tuples-only --no-align 2>&1
        if ($LASTEXITCODE -ne 0) {
            throw "PostgreSQL command failed: $($output -join [Environment]::NewLine)"
        }
        return @($output)
    }
    finally {
        if ($null -eq $previousPassword) {
            Remove-Item Env:PGPASSWORD -ErrorAction SilentlyContinue
        } else {
            $env:PGPASSWORD = $previousPassword
        }
    }
}

function ConvertTo-NpgsqlValue {
    param([Parameter(Mandatory)][string]$Value)
    return '"' + $Value.Replace('"', '""') + '"'
}

function New-NpgsqlConnectionString {
    param(
        [Parameter(Mandatory)][string]$HostName,
        [Parameter(Mandatory)][int]$Port,
        [Parameter(Mandatory)][string]$Database,
        [Parameter(Mandatory)][string]$UserName,
        [Parameter(Mandatory)][string]$Password
    )

    $hostValue = ConvertTo-NpgsqlValue $HostName
    $databaseValue = ConvertTo-NpgsqlValue $Database
    $userValue = ConvertTo-NpgsqlValue $UserName
    $passwordValue = ConvertTo-NpgsqlValue $Password
    return "Host=$hostValue;Port=$Port;Database=$databaseValue;Username=$userValue;Password=$passwordValue;Include Error Detail=false"
}
