[CmdletBinding()]
param(
    [string]$HostName = '127.0.0.1',
    [ValidateRange(1, 65535)][int]$Port = 5432,
    [string]$DatabaseName = 'ArksScannerDB',
    [string]$AdminUser = 'postgres',
    [string]$ApplicationRole = 'arksscanner_app',
    [Security.SecureString]$AdminPassword
)

. "$PSScriptRoot\Database.Common.ps1"
Assert-SafePostgresIdentifier $DatabaseName 'DatabaseName'
Assert-SafePostgresIdentifier $ApplicationRole 'ApplicationRole'
if (-not $AdminPassword) { $AdminPassword = Read-RequiredSecureString 'PostgreSQL administrator password' }

$psql = Find-Psql
$sql = @"
GRANT CONNECT ON DATABASE "$DatabaseName" TO "$ApplicationRole";
GRANT USAGE ON SCHEMA public TO "$ApplicationRole";
REVOKE CREATE ON SCHEMA public FROM "$ApplicationRole";
GRANT SELECT, INSERT, UPDATE, DELETE ON TABLE
    documents,
    pages,
    upload_intents,
    processing_jobs,
    document_exports,
    page_ocr_results,
    ocr_elements,
    page_revisions,
    text_edit_operations,
    page_signatures,
    signature_asset_write_intents,
    page_marks,
    page_repair_operations,
    accounts,
    subscriptions,
    admins,
    plan_settings,
    usage_days
TO "$ApplicationRole";
GRANT SELECT ON TABLE payments TO "$ApplicationRole";
GRANT SELECT, INSERT ON TABLE audit_events TO "$ApplicationRole";
REVOKE UPDATE, DELETE, TRUNCATE ON TABLE audit_events FROM "$ApplicationRole";
"@

Use-PlainText $AdminPassword {
    param($plainPassword)
    Invoke-Psql -PsqlPath $psql -HostName $HostName -Port $Port -Database $DatabaseName `
        -UserName $AdminUser -Password $plainPassword -Sql $sql | Out-Null
}

Write-Host "Applied least-privilege grants for $ApplicationRole on $DatabaseName."
