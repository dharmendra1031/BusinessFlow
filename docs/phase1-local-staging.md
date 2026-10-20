# Phase 1 — Safe local SQL Server staging rehearsal

**Status:** Script prepared for the user's Windows development computer. No access to that computer's SQL instance is available from GitHub; **no local SQL backup, restore, migration, or IIS deployment has been performed**.

## Purpose and safety boundaries

`scripts/prepare-local-staging.ps1` first runs a read-only connection/schema/file preflight. Unless you explicitly pass `-Execute` **and** approve the PowerShell confirmation, it does not perform a backup or restore.

Execution makes a COPY_ONLY, checksummed backup of the named source database, verifies the media and restores to a **new unique** `<SourceDatabase>_Stage_<random>` database and unique MDF/LDF filenames. There is no source ALTER, EF migration, DROP or RESTORE command. The script refuses an existing staging catalog or backup/target file, does not use RESTORE WITH REPLACE, and never deletes databases or backup files.

This is intended for a **local Windows SQL Server or named SQL Server instance** with Windows integrated authentication. It requires `sqlcmd`, a source database with a standard SQL Server data/log file layout and sufficient free disk space. The backup and MDF/LDF directories must already exist **on the SQL Server host**, and the SQL Server service account must be able to write to them. The Windows account must have backup and restore permissions. The script does not accept or store SQL passwords.

If your local server only supports SQL username/password, or uses LocalDB/unsupported file types, stop and get the connection/backup setup reviewed; do not put passwords into scripts or chat.

## Step 1: Identify your existing database without changing it

From PowerShell on the Windows machine:

```powershell
sqlcmd -S "localhost" -E -d master -Q "SELECT @@SERVERNAME AS ServerName; SELECT name FROM sys.databases ORDER BY name;"
```

Replace `localhost` with the exact instance, for example `localhost\SQLEXPRESS`, if needed. Confirm the actual CRM catalog; the repository's example catalog is `LeadManagement`, but your real local name may differ.

## Step 2: Prepare disk directories and run read-only preflight

Choose private local folders (sample: `D:\SqlBackups` and `D:\SqlStage`) on the **SQL Server machine**. A DBA can create them and grant the SQL Server service account the appropriate permissions. Check free disk space for a full .bak plus restored data and log files.

From the repository root:

```powershell
git switch development
git pull --ff-only origin development

.\scripts\prepare-local-staging.ps1 -Server "localhost" `
  -SourceDatabase "LeadManagement" `
  -BackupDirectory "D:\SqlBackups" `
  -DataDirectory "D:\SqlStage"
```

The initial run shows source database and logical files, plus proposed **unique** staging and backup names. It should report `PREFLIGHT ONLY`. The generated names change with each invocation.

**Send only the preflight output after redacting usernames, customer information or secrets.** Do not proceed if the source database, logical files, backup folder or SQL Server instance look wrong.

## Step 3: Explicit staging restore, only after reviewing preflight

The operator can then repeat the same arguments with `-Execute`:

```powershell
.\scripts\prepare-local-staging.ps1 -Server "localhost" `
  -SourceDatabase "LeadManagement" `
  -BackupDirectory "D:\SqlBackups" `
  -DataDirectory "D:\SqlStage" -Execute
```

The script requests a PowerShell high-impact confirmation. Confirm that the only **write** operations are COPY_ONLY backup to a new file and restore to a new database. If any command fails, do not retry blindly. It leaves recovery artifacts for DBA review, and never drops the staging or source database.

## Step 4: Compare records and migration baseline (read-only)

Use the actual staging database name printed by the script:

```sql
SELECT DB_NAME() AS DatabaseName;
SELECT MigrationId, ProductVersion FROM dbo.__EFMigrationsHistory ORDER BY MigrationId;
SELECT COUNT_BIG(*) AS Users FROM dbo.AspNetUsers;
SELECT COUNT_BIG(*) AS Leads FROM dbo.Leads;
SELECT COUNT_BIG(*) AS Projects FROM dbo.DevelopmentProjects;
SELECT COUNT_BIG(*) AS Tasks FROM dbo.DevelopmentTasks;
```

Run the same read-only counts on the source and on the staged clone. Compare at the backup point in time, including representative numeric IDs and role restrictions. If `__EFMigrationsHistory` is missing or differs from the expected baseline, **stop** and review legacy SQL upgrades before migrating.

Then, on **staging only**, after approving the migration baseline and retaining the backup, use the release-matched EF bundle or `dotnet ef database update` with an **explicit staging connection string**. Example for an authorized Windows-integrated local SQL connection:

```powershell
cd backend\BusinessFlow.Api
dotnet ef migrations list --connection "Server=localhost;Database=YOUR_GENERATED_STAGE_DATABASE;Integrated Security=True;Encrypt=True;TrustServerCertificate=True;"
dotnet ef database update --connection "Server=localhost;Database=YOUR_GENERATED_STAGE_DATABASE;Integrated Security=True;Encrypt=True;TrustServerCertificate=True;"
```

Both examples require the real generated staging database name and a migration-capable Windows identity; never substitute the original database. Use `TrustServerCertificate=True` **only for isolated local development** when using a local untrusted SQL certificate.

## Step 5: Validation and evidence

After applying staging migrations, verify `AddOrganization`, `AddPermissionGrants`, `AddDomainRowVersions`, and `AddLeadStatusApprovals` in `__EFMigrationsHistory`. Check record counts, rowversion, numeric IDs and foreign keys, and run:

```sql
DBCC CHECKDB (N'YOUR_GENERATED_STAGE_DATABASE') WITH NO_INFOMSGS, ALL_ERRORMSGS;
```

Use a **separate** locally running application instance pointing to staging for Admin/Manager/Employee/custom-role negative-access checks. Do not start the original app with a staging connection unless it is intentionally isolated. Verify Lead, Project, Task, Bug, Worklog, Approvals, Follow-up, Reports and login/logout behavior.

Record the staging database name, source migration baseline, backup file name/hash, migration IDs, pre/post counts, DBCC outcome, test/rollback results and operator sign-off in `RELEASE_RECORD.md`; never commit actual customer data, passwords, connection-string secrets or .bak files.

**Checkpoint definition:** SQL backup + separate restore + source comparison + staging-only migrations + DBCC + representative security workflows verified on the local machine. This is **not** a production VPS/HTTPS acceptance or a 100–200 concurrent-user load test.
