# Phase 1 — SQL staging recovery acceptance

**Status:** The CI restore drill uses disposable synthetic databases. A real staging/VPS backup-and-restore rehearsal is still pending. This procedure is not evidence of a live restore.

## Safety requirements

- Obtain the database owner's approval and identify the operator, change window, release commit, backup point, rollback contact and a genuinely isolated SQL Server staging target.
- Do not supply production passwords, secrets, or customer records in chat or GitHub. Restrict, sanitize and encrypt any customer-data backup used on staging.
- Staging must not use the production database name or IIS app pool. Never restore over an existing database and never use RESTORE WITH REPLACE.
- Protect a matching previous IIS ZIP, configuration and pre-release backup. Check free disk capacity and SQL Server service account permissions.
- Historical installations upgraded with database/20261011_srs_upgrade.sql may not have the correct EF migration history. Stop and reconcile their schema and EF history before running a migration bundle.

## 1. Source inventory (read-only)

Run against the source database using SSMS and keep the results in a restricted release record:

    SELECT DB_NAME() AS DatabaseName;
    SELECT MigrationId, ProductVersion FROM dbo.__EFMigrationsHistory ORDER BY MigrationId;
    SELECT COUNT_BIG(*) AS Leads FROM dbo.Leads;
    SELECT COUNT_BIG(*) AS Projects FROM dbo.DevelopmentProjects;
    SELECT COUNT_BIG(*) AS Tasks FROM dbo.DevelopmentTasks;

If an expected table or EF migration history is missing, investigate rather than applying migrations blindly. Record representative numeric IDs and role restrictions at the backup point in time.

## 2. Take a verified pre-release backup

On the source SQL Server host under an authorized Windows identity:

    .\scripts\backup-sqlserver.ps1 -Server "SOURCE_SQL_SERVER" -Database "LeadManagement" -BackupDirectory "D:\SqlBackups"

The script runs a COPY_ONLY backup with CHECKSUM and RESTORE VERIFYONLY. Record file path, backup timestamp, SHA-256, backup duration, permissions, and protected off-server copy. RESTORE VERIFYONLY alone is not a recovery test.

## 3. Recover to an entirely new staging database

Copy the approved, protected backup to the staging SQL Server. Inspect actual logical data/log names and media integrity first:

    USE [master];
    RESTORE FILELISTONLY FROM DISK = N'D:\SqlBackups\APPROVED_BACKUP.bak';
    RESTORE VERIFYONLY FROM DISK = N'D:\SqlBackups\APPROVED_BACKUP.bak' WITH CHECKSUM;
    GO

Example only, after replacing placeholders and confirming that the target database and file paths DO NOT already exist:

    IF DB_ID(N'LeadManagement_StageRestore') IS NOT NULL
        THROW 51000, 'Staging target already exists: abort.', 1;
    RESTORE DATABASE [LeadManagement_StageRestore]
      FROM DISK = N'D:\SqlBackups\APPROVED_BACKUP.bak'
      WITH MOVE N'ACTUAL_LOGICAL_DATA_NAME' TO N'D:\SqlStage\LeadManagement_StageRestore.mdf',
           MOVE N'ACTUAL_LOGICAL_LOG_NAME' TO N'D:\SqlStage\LeadManagement_StageRestore_log.ldf',
           RECOVERY, CHECKSUM, STATS = 10;
    GO
    DBCC CHECKDB (N'LeadManagement_StageRestore') WITH NO_INFOMSGS, ALL_ERRORMSGS;

If FILELISTONLY reports additional files, add a unique MOVE for each one. Do not issue any restore statement unless the destination is confirmed isolated, empty and safe. SQL Server needs write access to the target directories.

## 4. Reconcile source versus restored data

Before migration, inspect EF history and compare Leads, Projects, Tasks, role restrictions, permission grants, audit records, numeric IDs, and relationships against the SOURCE BACKUP TIME (not against later live changes). Confirm DBCC CHECKDB success.

## 5. Apply matching EF bundle on staging only

Using the bundle from the matching release commit and a separate schema migration Windows identity:

    .\efbundle.exe --connection "Server=STAGING_SQL_SERVER;Database=LeadManagement_StageRestore;Integrated Security=True;Encrypt=True;TrustServerCertificate=False;"

Ensure the certificate is trusted and the account has the expected migration rights. Confirm AddOrganization, AddPermissionGrants, AddDomainRowVersions and AddLeadStatusApprovals appear in the final EF history. Verify record counts, FK integrity, rowversion columns, and permission behavior. A second application of the same bundle should not change data. Stop and retain diagnostics if any step fails.

## 6. Separate IIS staging site and acceptance

Configure a dedicated staging IIS site and SQL application login against the upgraded staging clone (do not reuse schema migration credentials). Validate HTTPS readiness using:

    .\scripts\verify-release.ps1 -BaseUrl "https://STAGING_DOMAIN"

Using synthetic/sanitized test accounts, verify Admin, Manager, Employee and custom role access, scope revocation after team/department changes, Leads, Projects, Tasks, Bugs, Worklogs, Approvals, CSV/PDF Reports, audit history, cookie/CSRF/logout, readiness, IIS logs and error tracking. Do not run destructive test suites against shared staging without authorization and cleanup.

## 7. Record evidence and release decision

| Evidence | Actual result |
| --- | --- |
| Approved operator, change window and release SHA | Pending |
| IIS ZIP and EF bundle SHA-256 | Pending |
| Source migration baseline and backup file SHA-256 | Pending |
| Backup, RESTORE VERIFYONLY, isolated RESTORE, DBCC CHECKDB | Pending |
| Restored pre/post migration counts and sample numeric IDs | Pending |
| Applied migration IDs and bundle exit codes | Pending |
| HTTPS/IIS authentication, roles, UI and report smoke tests | Pending |
| Restore duration, RTO, RPO, rollback owner and previous ZIP | Pending |
| DBA, QA and release reviewer sign-off | Pending |

Link approved evidence in RELEASE_RECORD.md without committing secrets or customer data. Keep the staging clone until it is formally signed off and the rollback plan is accepted.

## CI evidence boundary

Phase1BackupRestoreTests runs against the temporary SQL Server CI instance when CRM_RESTORE_TEST=1. It can demonstrate synthetic backup, media verification, isolated restore and migration/record preservation. It cannot certify real customer backup retention, protected storage, actual staging IIS deployment, recovery-time objectives or production rollback.

**Phase 1 production acceptance remains pending until a real approved staging recovery drill is executed.**
