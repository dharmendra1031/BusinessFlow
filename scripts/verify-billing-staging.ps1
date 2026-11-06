param(
    [Parameter(Mandatory = $true)][string]$Server,
    [Parameter(Mandatory = $true)][string]$Database
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

if ([string]::IsNullOrWhiteSpace($Server)) { throw 'SQL Server name is required.' }
if ($Database -cnotmatch '^[A-Za-z][A-Za-z0-9_]{0,127}$') {
    throw 'Database name must be 1-128 letters, digits or underscores, beginning with a letter.'
}
if ($Database -in @('master', 'model', 'msdb', 'tempdb')) {
    throw 'A system database is not a BusinessFlow staging target.'
}
if (-not (Get-Command sqlcmd -ErrorAction SilentlyContinue)) {
    throw 'sqlcmd is required for a read-only staging verification.'
}

# Metadata and counts only: no migrations, updates, DDL, backups or restores.
$sql = @"
SET NOCOUNT ON;
IF DB_NAME() <> N'$Database'
    THROW 51200, 'Connected to a different database.', 1;
IF OBJECT_ID(N'dbo.__EFMigrationsHistory', N'U') IS NULL
    THROW 51201, 'EF migration history is missing.', 1;
IF NOT EXISTS (
    SELECT 1 FROM dbo.__EFMigrationsHistory
    WHERE MigrationId = N'20261105120000_AddFinancialAdjustmentApprovals'
)
    THROW 51202, 'Phase 6 financial approval migration has not been applied.', 1;
IF EXISTS (
    SELECT 1 FROM (VALUES
        (N'SalesContracts'), (N'SalesInvoices'), (N'SalesPayments'),
        (N'SalesCreditNotes'), (N'SalesRefunds'), (N'FinancialAdjustmentApprovals')
    ) AS required(TableName)
    WHERE NOT EXISTS (
        SELECT 1 FROM sys.tables t
        JOIN sys.schemas s ON s.schema_id = t.schema_id
        JOIN sys.columns c ON c.object_id = t.object_id
        WHERE s.name = N'dbo' AND t.name = required.TableName
          AND c.name = N'Id' AND c.is_identity = 1 AND TYPE_NAME(c.user_type_id) = N'bigint'
    )
)
    THROW 51203, 'Missing financial table or non-BIGINT identity key.', 1;
IF EXISTS (
    SELECT 1 FROM (VALUES
        (N'SalesContracts'), (N'SalesInvoices'), (N'FinancialAdjustmentApprovals')
    ) AS required(TableName)
    WHERE NOT EXISTS (
        SELECT 1 FROM sys.tables t
        JOIN sys.schemas s ON s.schema_id = t.schema_id
        JOIN sys.columns c ON c.object_id = t.object_id
        WHERE s.name = N'dbo' AND t.name = required.TableName
          AND c.name = N'RowVersion' AND TYPE_NAME(c.user_type_id) = N'timestamp'
    )
)
    THROW 51204, 'A required financial rowversion column is missing.', 1;
IF NOT EXISTS (
    SELECT 1 FROM sys.indexes
    WHERE object_id = OBJECT_ID(N'dbo.FinancialAdjustmentApprovals')
      AND name = N'IX_FinancialAdjustmentApprovals_InvoiceId_Kind'
      AND is_unique = 1 AND has_filter = 1 AND is_disabled = 0
)
    THROW 51205, 'Pending approval uniqueness index is missing.', 1;
IF NOT EXISTS (
    SELECT 1 FROM sys.indexes
    WHERE object_id = OBJECT_ID(N'dbo.SalesPayments')
      AND name = N'IX_SalesPayments_Reference'
      AND is_unique = 1 AND is_disabled = 0
)
    THROW 51206, 'Payment reference uniqueness index is missing.', 1;
IF NOT EXISTS (
    SELECT 1 FROM sys.indexes
    WHERE object_id = OBJECT_ID(N'dbo.SalesRefunds')
      AND name = N'IX_SalesRefunds_Reference'
      AND is_unique = 1 AND is_disabled = 0
)
    THROW 51207, 'Refund reference uniqueness index is missing.', 1;
IF EXISTS (
    SELECT 1 FROM (VALUES
        (N'SalesInvoices', N'CK_SalesInvoices_Amounts'),
        (N'SalesPayments', N'CK_SalesPayments_Amount'),
        (N'FinancialAdjustmentApprovals', N'CK_FinancialAdjustmentApprovals_State')
    ) AS required(TableName, ConstraintName)
    WHERE NOT EXISTS (
        SELECT 1 FROM sys.check_constraints c
        WHERE c.parent_object_id = OBJECT_ID(N'dbo.' + required.TableName)
          AND c.name = required.ConstraintName AND c.is_disabled = 0
    )
)
    THROW 51208, 'A required financial balance or approval constraint is missing.', 1;

SELECT N'MIGRATION', N'20261105120000_AddFinancialAdjustmentApprovals'
UNION ALL SELECT N'INVOICES', CAST(COUNT_BIG(*) AS nvarchar(30)) FROM dbo.SalesInvoices
UNION ALL SELECT N'PAYMENTS', CAST(COUNT_BIG(*) AS nvarchar(30)) FROM dbo.SalesPayments
UNION ALL SELECT N'CREDIT_NOTES', CAST(COUNT_BIG(*) AS nvarchar(30)) FROM dbo.SalesCreditNotes
UNION ALL SELECT N'REFUNDS', CAST(COUNT_BIG(*) AS nvarchar(30)) FROM dbo.SalesRefunds
UNION ALL SELECT N'APPROVALS', CAST(COUNT_BIG(*) AS nvarchar(30)) FROM dbo.FinancialAdjustmentApprovals;
"@

Write-Host "Read-only Phase 6 SQL checks: $Server / $Database"
& sqlcmd -S $Server -d $Database -E -b -W -s '|' -l 10 -t 30 -Q $sql
if ($LASTEXITCODE -ne 0) {
    throw "Phase 6 SQL staging verification failed (sqlcmd exit $LASTEXITCODE)."
}
Write-Host 'PASS: Phase 6 migration, numeric keys, concurrency and financial constraints.'
Write-Host 'No data was modified. This is not a backup/restore or production deployment.'
