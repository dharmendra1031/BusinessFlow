# Phase 4 operations: staging, backup and release checks

This is a runbook, **not evidence that a live environment was deployed, backed up
or restored**. Apply these steps first to a staging copy, not production.

## Pre-release checklist

1. Confirm the exact `development` commit and a successful SQL Server + browser CI run.
2. Build a Windows release using `scripts/publish-windows.ps1`. Verify that
   `LeadManagement-IIS.zip` contains `web.config`, React `wwwroot/index.html`,
   production placeholders and the matching `efbundle.exe`. Never ship real secrets.
3. Verify IIS out-of-process hosting, HTTPS bindings, host certificate, no public
   SQL Server port, and app pool access to only its site directory.
4. Back up the SQL Server database and VPS production configuration before
   deployment. Store both in a restricted, off-server backup location.
5. Apply the migration bundle to a staging database restored from the production
   backup. Run authentication, lead CRUD, project tasks/bugs/worklogs/planning,
   reports and audit permissions as Admin, Manager and Employee.
6. Repeat on a non-production IIS site with the exact release ZIP. Test secure
   cookies, CSRF rejection, sign-out, session expiration, HTTPS and error logging.
7. Record the release SHA, operators, migration result, test evidence, recovery
   point and rollback decision before a production deployment.

## SQL backup (run on the SQL Server host)

On a Windows SQL Server with `sqlcmd` installed, run under a Windows account
allowed to back up the CRM database. The backup directory must already exist
and must be writable by the **SQL Server service account**:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\backup-sqlserver.ps1 `
  -Server "localhost" -Database "LeadManagement" -BackupDirectory "D:\SqlBackups"
```

The script takes a **COPY_ONLY**, checksummed backup, then runs
`RESTORE VERIFYONLY`. It does not delete earlier backups or modify the CRM
database. Set up Windows Task Scheduler separately if automatic backups are
required, then monitor job failures and configure retention and encrypted,
off-server copies.

**Important:** `RESTORE VERIFYONLY` does not demonstrate that recovery works.
To test actual recovery, first inspect `RESTORE FILELISTONLY`, then restore
to a **different staging database name and file locations**, using the
correct logical data/log names from the backup. Never use `REPLACE` against
the live `LeadManagement` database. Validate row counts, database integrity
(`DBCC CHECKDB`) and login/API access against the restored database.

## Rollback

A database migration may be non-reversible without a full restore. Keep the
previous published ZIP and a pre-migration SQL backup as a matching pair.
For any failed staging release, stop the IIS app pool, restore the staging
database from the pre-release backup, redeploy the matching previous ZIP,
then validate `/health/ready` and representative real workflow tests. Practice
and time the restore before setting production recovery objectives.

## Logging and alerting

The API emits an `X-Request-Id` response header and logs unhandled exceptions
and server errors with request method, route path, status and request ID.
Do not log passwords, bearer tokens, cookies, SQL passwords or request bodies.
Use a Windows Event Log or central log collector to retain structured API
logs, with access control, retention and alert rules for repeated 5xx
responses, failed readiness checks and backup failures.

These are foundations only: centralized log collection, monitored backups,
actual staging restore, production secure sessions, and HTTPS release
acceptance still require environment-specific verification.
