# Phase 1 — Local SQL Server staging verification

**Environment:** Local SQL Server only. The Windows VPS staging checks are not yet applicable.

**Status:** Read-only preflight tooling prepared. No script in this guide has run on the user's PC.

## 1. First run the read-only preflight

Open PowerShell at the BusinessFlow repository root, update the checkout, then inspect the actual database name from local configuration.

~~~powershell
git switch development
git pull --ff-only origin development
.\scripts\check-local-staging.ps1 -Server 'localhost' -SourceDatabase 'LeadManagement' -StagingDatabase 'LeadManagement_Stage_20261021'
~~~

Replace server/database identifiers as required; do not share SQL passwords or local connection secrets. The script requires sqlcmd and Windows integrated SQL authentication. It executes **only metadata and SELECT queries**. It checks source availability, online status, EF migration history, current migrations and Leads/Projects/Tasks row counts. It rejects an already existing staging target and requires the distinct Source_Stage_* naming pattern.

Stop if the connection, migrations history or target-name validation fails. Do not apply migrations to the source database.

## 2. Backup source after approval

Confirm a non-public directory exists on the SQL Server machine, writable by its service identity with enough free disk space.

~~~powershell
.\scripts\backup-sqlserver.ps1 -Server 'localhost' -Database 'LeadManagement' -BackupDirectory 'D:\SqlBackups'
~~~

This existing script produces a COPY_ONLY backup with CHECKSUM and RESTORE VERIFYONLY. Backups can contain private customer data. Protect storage and access. VERIFYONLY **does not** verify a full restore.

## 3. Restore backup to a different, empty local database

In SQL Server Management Studio, inspect actual logical files:

~~~sql
USE [master];
RESTORE FILELISTONLY FROM DISK = N'D:\SqlBackups\ACTUAL_BACKUP_FILE.bak';
RESTORE VERIFYONLY FROM DISK = N'D:\SqlBackups\ACTUAL_BACKUP_FILE.bak' WITH CHECKSUM;
GO
~~~

Review every data/log file reported and choose unique MDF/LDF paths on the local SQL Server host. This is an illustration, NOT executable until replacing all placeholders and checking that the staging target is absent:

~~~sql
USE [master];
IF DB_ID(N'LeadManagement_Stage_20261021') IS NOT NULL
    THROW 51001, 'Staging database exists: stop, never overwrite it.', 1;

RESTORE DATABASE [LeadManagement_Stage_20261021]
  FROM DISK = N'D:\SqlBackups\ACTUAL_BACKUP_FILE.bak'
  WITH MOVE N'ACTUAL_DATA_LOGICAL_NAME'
         TO N'D:\SqlStaging\LeadManagement_Stage_20261021.mdf',
       MOVE N'ACTUAL_LOG_LOGICAL_NAME'
         TO N'D:\SqlStaging\LeadManagement_Stage_20261021_log.ldf',
       RECOVERY, CHECKSUM, STATS = 10;
GO
~~~

Add a MOVE for **each** logical data/log file if the backup has more than two. Never use WITH REPLACE, never remove an existing database to make room, and never restore over the source.

## 4. Check restored baseline before migrations

In SSMS against the staging copy:

~~~sql
USE [LeadManagement_Stage_20261021];
SELECT DB_NAME() AS DatabaseName;
SELECT MigrationId FROM dbo.__EFMigrationsHistory ORDER BY MigrationId;
SELECT COUNT_BIG(*) AS Leads FROM dbo.Leads;
SELECT COUNT_BIG(*) AS Projects FROM dbo.DevelopmentProjects;
SELECT COUNT_BIG(*) AS Tasks FROM dbo.DevelopmentTasks;
DBCC CHECKDB (N'LeadManagement_Stage_20261021') WITH NO_INFOMSGS, ALL_ERRORMSGS;
~~~

Compare migration history, row counts and representative BIGINT IDs against the source **at backup time**. A legacy SQL upgrade without the expected migration baseline requires reconciliation first.

## 5. Migrate only the isolated staging copy

After reviewing the restored baseline, use the EF CLI with an explicit staging connection (Windows Integrated auth example):

~~~powershell
cd backend\BusinessFlow.Api
dotnet ef migrations list --connection 'Server=localhost;Database=LeadManagement_Stage_20261021;Integrated Security=True;Encrypt=True;TrustServerCertificate=True;'
dotnet ef database update --connection 'Server=localhost;Database=LeadManagement_Stage_20261021;Integrated Security=True;Encrypt=True;TrustServerCertificate=True;'
cd ..\..
~~~

Before submitting the update command, read the **Database=** portion again; it must name the staging copy, never source. A matching migration bundle from the same GitHub commit is an alternative. Verify counts, IDs, role restrictions, rowversion columns and DBCC CHECKDB after upgrade. Run it a second time to confirm no new migrations are applied.

## 6. Local-only application smoke checks

Use separate staging application settings and a different local port without editing source application's credentials. Verify Login, roles, Leads, Follow-ups, Projects, Tasks, Bugs, Worklogs, Approvals and Reports with sanitized accounts. Do not run write-heavy Playwright tests on any shared/customer database without permission.

The CI synthetic backup/restore and 39 SQL/API tests are not proof that a Windows VPS deploy, live SQL recovery or HTTPS/IIS acceptance has happened.

## Evidence checklist

| Evidence | Current state |
| --- | --- |
| Local source preflight and EF history | Pending local execution |
| Source backup and VERIFYONLY | Pending local execution |
| Distinct staging database full RESTORE | Pending local execution |
| Data counts/IDs and DBCC CHECKDB | Pending local execution |
| Staging-only migrations and API workflows | Pending local execution |
| Windows VPS IIS/HTTPS acceptance | Pending future deployment |

Share only redacted non-sensitive preflight output and any errors. No passwords, API keys, JWTs, customer details or backup files need to be shared.
