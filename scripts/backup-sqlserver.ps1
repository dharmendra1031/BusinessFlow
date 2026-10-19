param(
    [Parameter(Mandatory = $true)][string]$Server,
    [Parameter(Mandatory = $true)][string]$Database,
    [Parameter(Mandatory = $true)][string]$BackupDirectory
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

if (-not (Get-Command sqlcmd -ErrorAction SilentlyContinue)) {
    throw 'sqlcmd is required. Install SQL Server Command Line Utilities on the database server.'
}
if ([string]::IsNullOrWhiteSpace($Database) -or $Database -match '[\[\]]') {
    throw 'Enter a SQL Server database name without brackets.'
}
if (-not (Test-Path -LiteralPath $BackupDirectory -PathType Container)) {
    throw 'BackupDirectory must exist and be writable by the SQL Server service account.'
}

$fileName = '{0}-{1}.bak' -f $Database, (Get-Date -Format 'yyyyMMdd-HHmmss')
$destination = Join-Path (Resolve-Path -LiteralPath $BackupDirectory).Path $fileName
$escapedName = $Database.Replace(']', ']]')
$escapedPath = $destination.Replace("'", "''")

# COPY_ONLY does not break the normal differential backup chain.
# CHECKSUM and RESTORE VERIFYONLY verify backup media, not a full recovery drill.
$sql = @"
BACKUP DATABASE [$escapedName]
TO DISK = N'$escapedPath'
WITH COPY_ONLY, INIT, CHECKSUM, STATS = 10;
RESTORE VERIFYONLY FROM DISK = N'$escapedPath' WITH CHECKSUM;
"@

& sqlcmd -S $Server -d master -E -b -Q $sql
if ($LASTEXITCODE -ne 0) {
    throw "Backup or media verification failed (sqlcmd exit code $LASTEXITCODE)."
}
Write-Host "Backup media verified: $destination"
Write-Host 'A separate restore to a test database is still required to verify recovery.'
