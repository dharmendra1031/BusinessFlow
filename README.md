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
