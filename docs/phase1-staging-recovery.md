# Phase 1 — SQL Server backup, restore and migration rehearsal

**Status:** isolated CI recovery rehearsal implemented; actual Windows VPS / customer staging restore **not yet accepted**. Do not use the CI test against a live customer database.

## CI recovery rehearsal

The `Phase1BackupRestoreTests` test in `tests/BusinessFlow.Tests/` runs only when `CRM_RESTORE_TEST=1`; the development SQL Server workflow enables it for its **disposable SQL Server 2022 service**. The test:

1. Creates a randomized `LeadManagement_CITests_Phase1_*` database at migration `20261019133000_AddRolePermissionOverrides` and inserts an existing manager, lead, project, task and role permission override.
2. Performs a `COPY_ONLY`, checksummed full backup on the test SQL Server; performs `RESTORE VERIFYONLY`.
3. Restores the backup under a **different** randomly generated `LeadManagement_CITests_Restore_*` database name, moving every logical data/log file to a new file name.
4. Runs all pending Entity Framework migrations **only on the restored copy**, then verifies the user/lead/project/task numeric IDs, existing Won lead status, populated rowversions, role override and applied migration history.
5. Applies the migrations a second time (idempotence), verifies that the original source database still has its old schema and lead record, and drops **only the generated restored database**.

**Safety boundary:** the fixture rejects database names that do not begin with `LeadManagement_CITests`. SQL backup and restore are executed only when explicitly enabled. The service account needs backup/restore privileges. The test database server's default SQL data directory is used for its temporary `.bak` file; the restore DB is dropped, but the backup file can remain on non-ephemeral test instances. In GitHub CI the SQL Server container is disposable. Avoid enabling the test on shared or production SQL instances.

In CI, see `.github/workflows/verify-development-ui.yml`. The regular SQL test step reports the full SQL/API count including this enabled recovery test. A local run requires an **isolated test SQL instance** and the project's existing `CRM_TEST_CONNECTION` guard in addition to `CRM_RESTORE_TEST=1`; it must never use customer secrets or data.

## Customer staging runbook — still needs execution

1. Identify the real Windows SQL Server instance, staging IIS site and distinct **staging** catalog. Confirm permission to use a sanitized copy of customer data; coordinate a maintenance window with the responsible administrator.
2. Confirm the release commit, package checksum and matching `efbundle.exe` from `release-manifest.json`. Do not use an unreviewed ZIP or change the production database connection while testing.
3. Make and retain a full **copy-only, checksummed backup** with `scripts/backup-sqlserver.ps1` on the database host. Save restricted off-server copies. Record the filename, operator and duration. `RESTORE VERIFYONLY` alone is insufficient.
4. On an isolated test SQL instance, first inspect the backup's `RESTORE FILELISTONLY` output. Restore it using a new staging database name and **new, separate MDF/LDF paths**. Do not use `WITH REPLACE` or point the restore at the live database.
5. Before upgrade, record `__EFMigrationsHistory`, row counts and sample record IDs for Leads, Projects, Tasks, Users and permissions. Apply the **matching** EF migration bundle only to the restored staging catalog. Check for legacy database modifications that are not represented by the migrations.
6. After migration, run `DBCC CHECKDB` on the restored catalog, compare counts/IDs and important relationships, and test Admin/Manager/Employee logins, role scoping, Leads, Follow-ups, Projects, Bugs, Worklogs, Planning, Reports and approvals.
7. Deploy the corresponding IIS package to the separate staging site with a restricted application SQL login. Run `scripts/verify-release.ps1` against the trusted staging HTTPS URL and test the relevant UI/API workflows.
8. Time and document a rollback rehearsal using the pre-upgrade backup **and** matching older binaries. Record evidence, operator, database names, checksums, timestamps and blockers in `RELEASE_RECORD.md`.

**Stop conditions:** backup failure, corrupt restore, SQL mismatch, missing data/foreign keys, incorrect role access, missing rollback point, failing staging HTTPS/security checks or unexpected EF migrations. Never claim production acceptance based only on CI or `RESTORE VERIFYONLY`.

**Evidence to collect:** test source/target database identifiers (no passwords), backup file hash, pre/post migration list, pre/post record counts, `DBCC CHECKDB` result, actual restore duration, staging login/permission tests, IIS package hash and operator sign-off.
