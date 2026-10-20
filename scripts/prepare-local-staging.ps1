[CmdletBinding(SupportsShouldProcess = $true, ConfirmImpact = 'High')]
param(
    [Parameter(Mandatory = $true)][string]$Server,
    [Parameter(Mandatory = $true)][string]$SourceDatabase,
    [Parameter(Mandatory = $true)][string]$BackupDirectory,
    [Parameter(Mandatory = $true)][string]$DataDirectory,
    [switch]$Execute
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

if (-not (Get-Command sqlcmd -ErrorAction SilentlyContinue)) {
    throw 'sqlcmd is required. Install the SQL Server command line tools first.'
}
if ($SourceDatabase -cnotmatch '^[A-Za-z][A-Za-z0-9_]{0,90}$') {
    throw 'SourceDatabase must be an explicit simple SQL database name (letters, numbers, underscore).'
}
foreach ($directory in @($BackupDirectory, $DataDirectory)) {
    if ($directory -notmatch '^[A-Za-z]:\\') {
        throw 'Directories must be absolute Windows drive paths on the SQL Server host.'
    }
}

function SqlLiteral([string]$value) { return "N'" + $value.Replace("'", "''") + "'" }
function SqlIdentifier([string]$value) { return '[' + $value.Replace(']', ']]') + ']' }

$source = SqlIdentifier $SourceDatabase
$sourceText = SqlLiteral $SourceDatabase
$runId = [guid]::NewGuid().ToString('N').Substring(0, 10)
$stagingDatabase = "${SourceDatabase}_Stage_$runId"
$staging = SqlIdentifier $stagingDatabase
$stagingText = SqlLiteral $stagingDatabase
$backupFile = Join-Path $BackupDirectory "${stagingDatabase}.bak"
$backupText = SqlLiteral $backupFile
$dataPath = $DataDirectory.TrimEnd('\', '/') + '\'
$dataPathText = SqlLiteral $dataPath

function Invoke-DatabaseSql([string]$query) {
    & sqlcmd -S $Server -d master -E -b -V 11 -t 600 -Q $query
    if ($LASTEXITCODE -ne 0) {
        throw "SQL operation failed (sqlcmd exit code $LASTEXITCODE). Review the staging-only operation; do not retry against the source database."
    }
}

$preflight = @"
SET NOCOUNT ON;
IF DB_ID($sourceText) IS NULL
    THROW 51000, 'Source database not found. No changes were made.', 1;
IF DB_ID($stagingText) IS NOT NULL
    THROW 51001, 'Staging target already exists. No changes were made.', 1;
IF EXISTS (SELECT 1 FROM sys.master_files WHERE database_id = DB_ID($sourceText) AND type NOT IN (0, 1))
    THROW 51002, 'Source has special file types. Use a DBA-managed staging restore.', 1;
SELECT name AS SourceDatabase, state_desc, recovery_model_desc
  FROM sys.databases WHERE name = $sourceText;
SELECT name AS LogicalFile, type_desc, physical_name
  FROM sys.master_files WHERE database_id = DB_ID($sourceText) ORDER BY file_id;
"@

Write-Host "Local SQL Server: $Server"
Write-Host "Source (backup only): $SourceDatabase"
Write-Host "New staging database: $stagingDatabase"
Write-Host "New backup (SQL Server disk): $backupFile"
Write-Host "Staging data files (SQL Server disk): $dataPath"
Write-Host 'Authentication: Windows integrated (-E). No SQL passwords are accepted by this script.'
Invoke-DatabaseSql $preflight

if (-not $Execute) {
    Write-Host ''
    Write-Host 'PREFLIGHT ONLY: no database backup, restore or migration was performed.'
    Write-Host 'Confirm SQL Server service account write access and sufficient free disk space.'
    Write-Host 'Re-run with -Execute to request an explicit PowerShell confirmation.'
    return
}

if (-not $PSCmdlet.ShouldProcess($stagingDatabase, "COPY_ONLY backup of $SourceDatabase, restore under a different database name")) {
    return
}

# The source is only backed up. No ALTER, RESTORE, DROP or EF migration targets it.
$sql = @"
SET NOCOUNT ON;
IF DB_ID($sourceText) IS NULL THROW 51003, 'Source database missing.', 1;
IF DB_ID($stagingText) IS NOT NULL THROW 51004, 'Target already exists: stop.', 1;
DECLARE @backup nvarchar(4000) = $backupText;
DECLARE @directory nvarchar(4000) = $dataPathText;
DECLARE @fileExists int = 0;
EXEC master.dbo.xp_fileexist @backup, @fileExists OUTPUT;
IF @fileExists <> 0 THROW 51005, 'Backup path already exists: refusing overwrite.', 1;

DECLARE @moves nvarchar(max) = N'';
DECLARE @logical nvarchar(128), @fileId int, @fileType tinyint;
DECLARE filelist CURSOR LOCAL FAST_FORWARD FOR
    SELECT name, file_id, type FROM sys.master_files
    WHERE database_id = DB_ID($sourceText) ORDER BY file_id;
OPEN filelist;
FETCH NEXT FROM filelist INTO @logical, @fileId, @fileType;
WHILE @@FETCH_STATUS = 0
BEGIN
    IF @fileType NOT IN (0, 1) THROW 51006, 'Special SQL file type requires manual DBA restore.', 1;
    DECLARE @physical nvarchar(4000) = @directory + $stagingText + N'_' +
        CONVERT(nvarchar(20), @fileId) + CASE WHEN @fileType = 1 THEN N'.ldf' ELSE N'.mdf' END;
    SET @fileExists = 0;
    EXEC master.dbo.xp_fileexist @physical, @fileExists OUTPUT;
    IF @fileExists <> 0 THROW 51007, 'Target data file exists: refusing overwrite.', 1;
    SET @moves += CASE WHEN LEN(@moves) > 0 THEN N', ' ELSE N'' END +
        N'MOVE N''' + REPLACE(@logical, N'''', N'''''') +
        N''' TO N''' + REPLACE(@physical, N'''', N'''''') + N'''';
    FETCH NEXT FROM filelist INTO @logical, @fileId, @fileType;
END;
CLOSE filelist;
DEALLOCATE filelist;
IF LEN(@moves) = 0 THROW 51008, 'No SQL data files found.', 1;

BACKUP DATABASE $source TO DISK = $backupText WITH COPY_ONLY, INIT, CHECKSUM, STATS = 10;
RESTORE VERIFYONLY FROM DISK = $backupText WITH CHECKSUM;
-- For a backup of this very source instance, sys.master_files provides the logical names.
RESTORE FILELISTONLY FROM DISK = $backupText;
IF DB_ID($stagingText) IS NOT NULL THROW 51009, 'Target appeared during backup: refusing overwrite.', 1;
DECLARE @restoreSql nvarchar(max) = N'RESTORE DATABASE $staging FROM DISK = N''' +
    REPLACE(@backup, N'''', N'''''') + N''' WITH ' + @moves + N', RECOVERY, CHECKSUM, STATS = 10;';
EXEC sys.sp_executesql @restoreSql;
SELECT name AS DatabaseName, state_desc FROM sys.databases WHERE name = $stagingText;
"@

Invoke-DatabaseSql $sql
Write-Host ''
Write-Host "RESTORE FINISHED: $stagingDatabase"
Write-Host "Backup file: $backupFile"
Write-Host 'No migrations were applied. Verify restored counts and EF history before upgrading.'
Write-Host 'Do not remove the database or backup until recovery evidence is reviewed.'
