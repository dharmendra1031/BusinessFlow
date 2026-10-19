# Lead Management CRM

Single-company lead management system for admins, managers and employees.

## Stack

- ASP.NET Core Web API (.NET 10)
- Entity Framework Core / SQL Server
- ASP.NET Core Identity (`long` numeric user/role keys)
- React + TypeScript + Vite

## Features included in this starter

- JWT sign-in and role-based API authorization
- Admin-managed employees, optional reporting manager
- Lead create/read/update, search, filters and pagination
- Lead assignment with pending follow-up reassignment and assignment history
- Status changes and status history
- Follow-ups, completion and overdue list
- Dashboard counts scoped to each user's permissions
- React dashboard, lead listing, lead editing, and follow-up scheduling

## Local setup

Requirements: .NET 10 SDK, Node.js 22+, SQL Server, and EF Core CLI (`dotnet tool install --global dotnet-ef --version 10.0.0`).

1. Development settings are in `backend/LeadManagement.Api/appsettings.Development.json`. It contains the LocalDB connection, JWT key, and bootstrap admin credentials. Change the credentials before sharing this repository.
2. Production settings are in `backend/LeadManagement.Api/appsettings.Production.json`. Replace its SQL Server and JWT placeholders with real deployment values before running in Production. `appsettings.json` contains settings common to both environments.
3. In `backend/LeadManagement.Api`, run (design-time factory reads the configured connection string):

   ```powershell
   dotnet restore
   dotnet ef database update
   dotnet run
   ```

4. In `frontend`, run:

   ```powershell
   npm install
   npm run dev
   ```

5. Open the Vite URL and sign in with the bootstrap admin credentials.

The admin is seeded only if `CRM_ADMIN_EMAIL` and `CRM_ADMIN_PASSWORD` are configured and the database has no admin user. Development credentials in this repository are for local use only; replace them before sharing the repository. Never commit production credentials.

## Roles

- `Admin`: all leads, assignments, employee provisioning, full dashboard.
- `Manager`: own and direct reports' leads, assign only to direct reports or self.
- `Employee`: own/created leads and follow-ups.

## In scope / pending

This is **Phase 1 starter**, not a production-complete CRM. Lead deletion and automated integrations are not included yet. Meta Lead Ads, WhatsApp webhooks, bulk Excel import, reminders, attachments, and notifications are pending. The frontend is a basic MVP; the backend has additional functionality available for UI expansion. An IIS deployment package exists, but live deployment and broader production validation are pending.

Security hardening in this starter includes login rate limiting (five attempts per minute per remote IP), disabled-user checks, security-stamp and role revalidation for JWTs, HSTS outside development, and liveness/readiness endpoints. These are source changes, not verified deployment controls. Old tokens expire immediately after the security-stamp claim is introduced; users must sign in again.

Do not deploy publicly until HTTPS/proxy configuration, secrets management, tested migrations, observability, and secure cookie-based web authentication have been reviewed.

## UI feedback and accessibility

The frontend uses a shared feedback provider: success/error/warning notifications and keyboard-accessible confirmations for lead assignment, terminal status changes, and follow-up completion. It also includes responsive navigation and lead/employee drawers. This is a UI implementation, not evidence of production security or completed browser testing.

## Kuwait SRS extensions (feature branch)

Customer entries now include a database-generated numeric ID, separate immutable
`CUS-YYYY-000001` display code, address, lead referrer, KWD project amount,
last and next follow-up summaries, and optional SRS lead statuses.
Lead sources remain separate from the person who provided the lead.

- Lead screen: customer details, amount, status, and per-customer follow-up history.
- Admin: employee maintenance, activation/deactivation, secure reset password, and totals.
- Account: change own password.
- Dashboard: project value, follow-ups, and active/closed/not-interested counts.
- Reports: ten SRS categories with date, status, referrer, and permitted user filters.
  Results are capped at 5,000 rows per request; CSV only exports returned rows.
- Audit: lead create/edit/status/assignment, follow-up edits, and user management.
  Password contents are never included in the audit trail.

Existing SQL Server installations must back up their database before applying
`database/20261011_srs_upgrade.sql`. Do not apply this upgrade to an empty
database: generate the EF Core initial migration from the current model instead.
Existing installations with an EF migration history should reconcile their
migration baseline before future `dotnet ef database update` calls.

The SQL Server integration test, API build, and frontend build pass locally
against the new schema. Live deployment, security testing, PDF export, and
unrestricted report pagination are not verified. See [Windows VPS deployment](DEPLOY_WINDOWS.md)
for the single-domain IIS package and setup steps.

## Development project management (Phase 1)

Lead management stays intact. A **Development projects** workspace now handles delivery after a sale:

- Convert a **Won** lead into one development project (duplicate conversion is blocked).
- Record project title, description, owner, start/due dates and delivery status.
- Organize project tasks with title, notes, priority, assignee, due date, and status.
- Edit tasks, mark them In Progress/Blocked/Done, and view task-based completion percentage.
- Only complete a project after all tasks are done. Development actions are audited.
- Admin has full access; Manager access is restricted to projects they manage and their team;
  employees can view projects assigned to them and update only their assigned task statuses.
- Data uses BIGINT auto-increment primary keys, SQL Server foreign keys, and the additive
  EF Core migration `20261018150000_AddDevelopmentProjects`.

Back up an existing SQL Server database before applying the new migration with
`dotnet ef database update`. Do not run migrations on a live database without a backup
and deployment review. Phase 1 originally excluded daily worklogs and sprint planning; both are implemented in
Phases 2 and 3 below. File attachments, Git integration, release/deployment tracking,
and client approval workflows remain pending.

## Developer worklogs and bug tracking (Phase 2)

Each development project now includes a **Bug tracking** list and **Daily worklogs** in its existing project drawer.

- Bugs: title, description, severity (Low/Medium/High/Critical), linked task, developer assignment, and Open/In Progress/Resolved/Closed status. Managers/Admins triage, edit, assign and close; developers can report bugs and update only their assigned issues to In Progress/Resolved.
- Daily worklogs: developers and project owners can record their **own** work date (UTC, last 30 days), 0.25–24 hours per entry (up to 2 decimals), task association and 2–1,000 character summary. Total across a developer's projects cannot exceed 24 hours per UTC date.
- Project managers and admins see the project's team worklogs; other developers see only their own worklogs. Entries are append-only and creation is audited. Up to 500 recent bugs/worklog entries appear in each list; displayed hours summarize the returned rows, not necessarily all historical time.
- Projects cannot be marked Completed while open bugs remain or any task is incomplete. Developers cannot reopen Closed bugs. A user assigned a bug can access its project even without another assigned task.
- New SQL Server tables: `ProjectBugs` and `DeveloperWorklogs` with BIGINT identity IDs and foreign keys, introduced in additive EF migration `20261019070000_AddDevelopmentActivity`.
- Run `dotnet ef database update` **only after backing up a live database**. Confirm migration state and SQL schema before deployment.

Phase 3 below adds sprint and milestone planning. GitHub sync, attachments,
dedicated sprint boards and automated time capture remain pending. Production deployment
and cross-browser testing are not yet verified.

## Sprint and milestone planning (Phase 3)

The **Development projects** drawer now includes two types of delivery planning:

- **Milestones** represent delivery goals, with title, optional description, deadline and Planned/In Progress/Completed status.
- **Sprints** represent timeboxed work, with name, optional goal, start/end dates, status and progress.
- Managers/Admins can create and edit project planning items. Developers can see planning for the projects they are allowed to access.
- Development tasks can be linked to **one milestone and one sprint**. The API rejects cross-project IDs, completed planning items and task deadlines later than linked sprint/milestone deadlines.
- Planning progress displays completed task count and percentage. An empty or unfinished sprint/milestone cannot be marked Completed. Completing a whole project also requires every planned sprint/milestone to be Completed, on top of the existing finished-tasks and closed-bugs rules.
- Project date edits cannot invalidate existing sprint/milestone boundaries.
- Adds SQL Server tables `ProjectMilestones`, `ProjectSprints` and nullable `MilestoneId`/`SprintId` fields on `DevelopmentTasks`, using BIGINT identity IDs and an additive EF Core migration `20261019100000_AddProjectPlanning`. Existing tasks remain unassigned to a sprint/milestone until a manager links them.

Back up live SQL Server databases and review migration history before applying `dotnet ef database update`. Production deployment and a full security/performance audit remain pending.

## CI and dependency security updates (19 October 2026)

- Development CI runs migrations against SQL Server, a backend permission integration test,
  NuGet transitive dependency audit, React build, and Chromium Playwright regression tests.
- The release verification workflow now applies **committed** EF migrations from
  `backend/LeadManagement.Api/Migrations`. It no longer creates an extra `InitialCreate`
  or uploads generated migrations. It checks EF model snapshot consistency.
- ASP.NET Core/EF Core packages use patched 10.0.13 versions. The API explicitly
  references `System.Security.Cryptography.Pkcs` 10.0.13 to override the previously
  reported vulnerable transitive dependency. Both CI pipelines contain a script-based
  vulnerability check.
- Publishing and actual IIS deployment are separate activities: a successful Linux CI
  run does not prove that a Windows publish ZIP has been built, installed or restored
  successfully on a VPS.

See `PROJECT_AUDIT.md` for remaining production security, operational and deployment gaps.

## Report delivery update

All ten report categories now have server-side pagination (50 rows by default,
200 maximum), filtered full CSV export, and generated RDLC templates with typed
datasets. PDF preview uses a packaged Windows .NET Framework 4.8 renderer.
The RDLC definitions are generated from `reporting/ReportRenderer/Reports/definitions.json`
using `scripts/generate-rdlc.py`; run `scripts/verify-rdlc.py` on Windows after
changes. Reports, projects, eligible won leads, follow-ups, bugs and worklogs
now return paged results. Clients using the earlier array response must update
to read `items`, `total`, `page` and `pageSize` together. Full deployment and
staging acceptance are still pending.

## Phase 4 — production foundation (development branch)

The following safeguards are now implemented in source:

- **Browser authentication:** Production uses a signed JWT stored in a
  `HttpOnly`, `Secure`, `SameSite=Strict` cookie. The frontend restores
  sessions through `/api/auth/me` and does not persist production bearer tokens
  in `sessionStorage`. Cookie-authenticated writes require ASP.NET Core
  antiforgery tokens. The separate development/API bearer-token path remains
  supported for existing tooling and browser tests.
- **Role permissions:** Admins can block supported Manager/Employee module
  permissions and restore their original access. Permissions cannot grant
  privileges beyond the existing server-side role, team and ownership rules.
  API routes enforce the overrides. A new `RolePermissionOverrides` SQL table
  (BIGINT identity, unique role/permission index) is introduced through
  migration `20261019133000_AddRolePermissionOverrides`.
- **Audit history:** A dedicated Admin-only screen reads existing audit
  entries with date/user/module/action filters and server-side pagination.
- **Diagnostics:** Failed API requests and exceptions produce structured
  server log entries and an `X-Request-Id` correlation header.
- **Operational guides:** `OPERATIONS.md`, `PHASE4_PRODUCTION_CHECKLIST.md` and `scripts/backup-sqlserver.ps1`
  document a checksum-verified copy-only SQL backup; the separate release
  verification script checks a deployed IIS instance without writing data.

**Not complete:** Production IIS deployment and HTTPS acceptance, a real
SQL backup/restore drill, centralized log collection/alerts, advanced MFA/SSO
and a full security/load review still need real environment verification.
Keep `main` unchanged until release gates and staging checks are accepted.
