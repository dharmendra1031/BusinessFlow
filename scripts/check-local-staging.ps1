param(
    [Parameter(Mandatory = $true)][string]$Server,
    [Parameter(Mandatory = $true)][string]$SourceDatabase,
    [Parameter(Mandatory = $true)][string]$StagingDatabase
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

# Read-only preflight. No BACKUP, RESTORE, CREATE, DROP or EF migration commands.
# Restrict database identifiers rather than interpolating arbitrary SQL input.
foreach ($name in @($SourceDatabase, $StagingDatabase)) {
    if ($name.Length -gt 128 -or $name -cnotmatch '^[A-Za-z][A-Za-z0-9_]*$') {
        throw 'Database names must be 1-128 characters containing only letters, digits and underscores, beginning with a letter.'
    }
}
$prefix = $SourceDatabase + '_Stage_'
if (-not $StagingDatabase.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase)) {
    throw "StagingDatabase must start with '$prefix' and must be a different, newly named database."
}
if ([string]::IsNullOrWhiteSpace($Server)) { throw 'Server is required.' }
if (-not (Get-Command sqlcmd -ErrorAction SilentlyContinue)) {
    throw 'sqlcmd is required. Install the SQL Server Command Line Utilities and retry.'
}

# All database names were checked above. This SQL only reads metadata and counts.
$sql = @"
SET NOCOUNT ON;
IF DB_ID(N'$SourceDatabase') IS NULL
    THROW 51001, 'Source database does not exist or is not visible to the current SQL login.', 1;
IF NOT EXISTS (SELECT 1 FROM sys.databases WHERE name = N'$SourceDatabase' AND state_desc = N'ONLINE')
    THROW 51002, 'Source database is not online.', 1;
IF DB_ID(N'$StagingDatabase') IS NOT NULL
    THROW 51003, 'Staging target already exists: do not overwrite or reuse it.', 1;
IF OBJECT_ID(N'[$SourceDatabase].dbo.__EFMigrationsHistory', N'U') IS NULL
    THROW 51004, 'Source EF migrations history is missing. Reconcile legacy upgrades before continuing.', 1;

SELECT N'SOURCE_DATABASE' AS CheckName, CAST(name AS nvarchar(128)) AS Detail
    FROM sys.databases WHERE name = N'$SourceDatabase';
SELECT N'RECOVERY_MODEL' AS CheckName, CAST(recovery_model_desc AS nvarchar(128)) AS Detail
    FROM sys.databases WHERE name = N'$SourceDatabase';
SELECT N'STAGING_TARGET_AVAILABLE' AS CheckName, N'$StagingDatabase' AS Detail;
EXEC(N'SELECT N''MIGRATION'' AS CheckName, CAST(MigrationId AS nvarchar(200)) AS Detail
    FROM [$SourceDatabase].dbo.__EFMigrationsHistory ORDER BY MigrationId;');
IF OBJECT_ID(N'[$SourceDatabase].dbo.Leads', N'U') IS NOT NULL
    EXEC(N'SELECT N''LEADS'' AS CheckName, CAST(COUNT_BIG(*) AS nvarchar(30)) AS Detail
        FROM [$SourceDatabase].dbo.Leads;');
IF OBJECT_ID(N'[$SourceDatabase].dbo.DevelopmentProjects', N'U') IS NOT NULL
    EXEC(N'SELECT N''PROJECTS'' AS CheckName, CAST(COUNT_BIG(*) AS nvarchar(30)) AS Detail
        FROM [$SourceDatabase].dbo.DevelopmentProjects;');
IF OBJECT_ID(N'[$SourceDatabase].dbo.DevelopmentTasks', N'U') IS NOT NULL
    EXEC(N'SELECT N''TASKS'' AS CheckName, CAST(COUNT_BIG(*) AS nvarchar(30)) AS Detail
        FROM [$SourceDatabase].dbo.DevelopmentTasks;');
"@

Write-Host "Read-only SQL staging preflight: $Server"
Write-Host "Source: $SourceDatabase"
Write-Host "Proposed new staging database: $StagingDatabase"
& sqlcmd -S $Server -d master -E -b -W -h -1 -s '|' -l 10 -t 30 -Q $sql
if ($LASTEXITCODE -ne 0) {
    throw "Preflight failed (sqlcmd exit code $LASTEXITCODE). No database was changed."
}
Write-Host 'PASS: source is online, EF migration history exists and the staging target is unused.'
Write-Host 'No backup, restore or migration was executed. Review the results before cloning the database.'
