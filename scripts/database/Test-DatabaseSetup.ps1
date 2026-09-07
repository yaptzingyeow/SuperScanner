[CmdletBinding()]
param(
    [string]$HostName = '127.0.0.1',
    [ValidateRange(1, 65535)][int]$Port = 5432,
    [string]$DatabaseName = 'SuperScannerDB',
    [string]$AdminUser = 'postgres',
    [string]$ApplicationRole = 'superscanner_app',
    [Security.SecureString]$AdminPassword
)

. "$PSScriptRoot\Database.Common.ps1"
Assert-SafePostgresIdentifier $DatabaseName 'DatabaseName'
Assert-SafePostgresIdentifier $ApplicationRole 'ApplicationRole'
if (-not $AdminPassword) { $AdminPassword = Read-RequiredSecureString 'PostgreSQL administrator password' }

$psql = Find-Psql
$sql = @"
WITH required_tables(name) AS (
    VALUES ('documents'), ('pages'), ('upload_intents'), ('processing_jobs'), ('audit_events'), ('__EFMigrationsHistory')
), required_migrations(id) AS (
    VALUES
      ('20260902013508_InitialSchema'),
      ('20260903153822_QuarantinedUploadIntents'),
      ('20260903155654_UploadValidationJobs'),
      ('20260904012909_LeasedProcessingJobs'),
      ('20260904015937_TamperEvidentAuditChain')
), required_indexes(name) AS (
    VALUES
      ('IX_documents_OwnerFirebaseUid_UpdatedAt'),
      ('IX_pages_DocumentId_PageNumber'),
      ('IX_upload_intents_IdempotencyKey'),
      ('IX_processing_jobs_Status_AvailableAt_CreatedAt'),
      ('IX_audit_events_TargetId_Sequence')
), issues AS (
    SELECT 'missing table: ' || name AS issue
      FROM required_tables
     WHERE to_regclass('public.' || quote_ident(name)) IS NULL
    UNION ALL
    SELECT 'missing migration: ' || id
      FROM required_migrations
     WHERE NOT EXISTS (SELECT 1 FROM "__EFMigrationsHistory" h WHERE h."MigrationId" = id)
    UNION ALL
    SELECT 'missing index: ' || name
      FROM required_indexes
     WHERE to_regclass('public.' || quote_ident(name)) IS NULL
    UNION ALL
    SELECT 'missing app privilege: ' || privilege || ' on ' || table_name
      FROM (VALUES
        ('documents', 'SELECT'), ('documents', 'INSERT'), ('documents', 'UPDATE'), ('documents', 'DELETE'),
        ('pages', 'SELECT'), ('pages', 'INSERT'), ('pages', 'UPDATE'), ('pages', 'DELETE'),
        ('upload_intents', 'SELECT'), ('upload_intents', 'INSERT'), ('upload_intents', 'UPDATE'), ('upload_intents', 'DELETE'),
        ('processing_jobs', 'SELECT'), ('processing_jobs', 'INSERT'), ('processing_jobs', 'UPDATE'), ('processing_jobs', 'DELETE'),
        ('audit_events', 'SELECT'), ('audit_events', 'INSERT')
      ) permissions(table_name, privilege)
     WHERE NOT has_table_privilege('$ApplicationRole', 'public.' || table_name, privilege)
    UNION ALL
    SELECT 'forbidden app privilege: ' || privilege || ' on audit_events'
      FROM (VALUES ('UPDATE'), ('DELETE'), ('TRUNCATE')) forbidden(privilege)
     WHERE has_table_privilege('$ApplicationRole', 'public.audit_events', privilege)
)
SELECT issue FROM issues ORDER BY issue;
"@

$issues = Use-PlainText $AdminPassword {
    param($plainPassword)
    Invoke-Psql -PsqlPath $psql -HostName $HostName -Port $Port -Database $DatabaseName `
        -UserName $AdminUser -Password $plainPassword -Sql $sql
}
$issues = @($issues | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
if ($issues.Count -gt 0) {
    $details = $issues -join [Environment]::NewLine
    throw "Database verification failed:$([Environment]::NewLine)$details"
}

Write-Host "Verified schema, five migrations, indexes, and $ApplicationRole permissions on $DatabaseName."
