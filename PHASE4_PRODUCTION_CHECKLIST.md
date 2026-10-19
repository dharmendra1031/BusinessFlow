# Phase 4: production-readiness acceptance checklist

**Status:** development implementation only. Complete these checks on staging before using real customer data. Successful CI does not certify the Windows VPS or its SQL Server.

## Authentication and access

- Serve both React and /api on the **same HTTPS domain**. Production login sets an HttpOnly, Secure, SameSite=Strict `CRM.Session` cookie; the login JSON intentionally does not expose the JWT. The React client restores user identity through `GET /api/auth/me` and sends the CSRF token with modifying API requests.
- Verify that browser storage contains **no `crmSession` JWT** in a Production build. Existing development-only compatibility for bearer tokens is not evidence of production isolation.
- Test CSRF rejection, invalid credentials, role changes, employee deactivation, password changes, sign-out, session expiration, and requests from another origin. Review every permission-sensitive endpoint.
- Check CSP, HSTS, X-Content-Type-Options, X-Frame-Options and Referrer-Policy using the real HTTPS domain. Assess browser console for blocked legitimate assets and scripts.
- MFA/SSO, configurable module-level permissions, and an independent security assessment are **not implemented** and remain separate acceptance items.

## Database backup and recovery

1. Inventory and protect SQL Server credentials, TLS certificate, the production settings file, the application ZIP and the matching migration bundle. Only permitted staff should access them.
2. Before each release, take a full SQL backup to storage **outside** the IIS application folder. An example in SSMS (update the filename and ensure SQL Server's service account can write there):

   ```sql
   BACKUP DATABASE [LeadManagement]
     TO DISK = N'D:\SqlBackups\LeadManagement_before_release.bak'
     WITH COPY_ONLY, COMPRESSION, CHECKSUM, INIT;
   RESTORE VERIFYONLY
     FROM DISK = N'D:\SqlBackups\LeadManagement_before_release.bak'
     WITH CHECKSUM;
   ```

3. Copy the backup to protected off-server storage; record backup duration, size and permissions. Encryption and retention policies must be decided for the customer's infrastructure.
4. **Test restore on an isolated staging database**, not the live `LeadManagement` database. Use `RESTORE FILELISTONLY` to identify its logical file names before a `RESTORE DATABASE [LeadManagement_RestoreTest] ... WITH MOVE ...`; confirm data and schema, then document recovery time.
5. Perform a fresh-database migration rehearsal and an upgrade rehearsal using a copy of the **current production schema**. Reconcile older installations with SQL upgrade scripts against EF migration history before applying an EF bundle.
6. Document rollback: save the exact pre-release binaries/config, and decide whether a point-in-time database restore is necessary. Do not roll back binaries across incompatible schema changes without testing.

## Windows IIS staging acceptance

- Publish using `scripts/publish-windows.ps1` on a Windows build host. Inspect `web.config` for `hostingModel="outofprocess"`, and ensure `wwwroot/index.html` is present.
- Verify ZIP contents have no development settings, plaintext production secrets, or stale appsettings copied from a prior release.
- Install matching .NET 10 Hosting Bundle. Use a dedicated app pool with least privilege, SQL application login with data access only, and a separate migration identity.
- Verify TLS certificate hostname/trust, HTTPS redirect/HSTS, `/health/live`, `/health/ready`, sign-in, restricted pages, file permissions and auth cookie attributes.
- Test Leads, Follow-ups, Projects, Tasks, Bugs, Worklogs, Planning, Reports and Admin Audit using real staging SQL Server.
- Confirm request correlation via `X-Request-Id` and structured server-side error logs. Connect a centralized log sink/retention/alert service **before** operational acceptance.
- Run production-configuration smoke tests, a load test representative of expected users, a database restore test and an emergency rollback drill.

## CI gates

The development pipeline checks SQL Server EF migrations and model snapshot, permission integration tests, NuGet vulnerability audit, frontend production build and Chromium browser tests. The release workflow is stored on `development` but still needs to run on a real pull request into `main` and on the intended release candidate.

**Do not declare production-ready until the staging and live acceptance evidence above is recorded.**
