# Phase 4 operations: staging, backup and release checks

This is a runbook, **not evidence that a live environment was deployed, backed up
or restored**. Apply these steps first to a staging copy, not production.

## Pre-release checklist

1. Confirm the exact `development` commit and a successful SQL Server + browser CI run.
2. Build a Windows release using `scripts/publish-windows.ps1`. Verify that
   `BusinessFlow-IIS.zip` contains `web.config`, React `wwwroot/index.html`,
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

Use `RELEASE_RECORD.md` for each release candidate. A separate Windows staging
IIS site and SQL Server database are required before production use. Give its
app pool read/execute access only to its site, use a data-only SQL login for the
API and a separate migration identity, and bind a trusted HTTPS certificate.
Use approved sanitized data for upgrade rehearsals. Do not point staging at
the production database.

`release-manifest.json` contains the candidate commit and SHA-256 values for
the immutable IIS ZIP and EF bundle. The ZIP includes the .NET Framework 4.8
RDLC renderer and ten templates. Install .NET Framework 4.8 on the Windows
host. Run `scripts/verify-rdlc.py` on the Windows build machine and verify PDF
preview for all report categories on staging. The UI table and CSV use the
same server-side filters and role scope. Report PDF rendering has a 5,000-row
limit; CSV has a 100,000-row limit by default. Narrow filters when a limit is
reached. `Reports:MaxPdfRows`, `Reports:MaxExportRows` and
`Reports:RendererPath` can be set in protected production configuration.

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

Assign a named alert owner and set retention in the infrastructure log
collector. Record a test alert receipt in `RELEASE_RECORD.md`. Stop deployment
if request errors or SQL readiness cannot be observed by that owner.

These are foundations only: centralized log collection, monitored backups,
actual staging restore, production secure sessions, and HTTPS release
acceptance still require environment-specific verification.
