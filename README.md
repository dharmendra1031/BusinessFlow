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
- Basic React dashboard and lead screens

## Local setup

Requirements: .NET 10 SDK, Node.js 22+, SQL Server, and EF Core CLI (`dotnet tool install --global dotnet-ef --version 10.0.0`).

1. Configure a local SQL Server connection in `backend/LeadManagement.Api/appsettings.json` or via environment variable `ConnectionStrings__DefaultConnection`.
2. Set environment variables `Jwt__Key` (a random secret of at least 32 bytes), `CRM_ADMIN_EMAIL`, and `CRM_ADMIN_PASSWORD` (12+ characters with upper/lower-case letters, numbers and symbols).
3. In `backend/LeadManagement.Api`, run:

   ```powershell
   dotnet restore
   dotnet ef migrations add InitialCreate
   dotnet ef database update
   dotnet run
   ```

4. In `frontend`, run:

   ```powershell
   npm install
   npm run dev
   ```

5. Open the Vite URL and sign in with the bootstrap admin credentials.

The admin is seeded only if `CRM_ADMIN_EMAIL` and `CRM_ADMIN_PASSWORD` are set and the database has no admin user. Remove the environment variables after initial setup. **Never commit credentials.**

## Roles

- `Admin`: all leads, assignments, employee provisioning, full dashboard.
- `Manager`: own and direct reports' leads, assign only to direct reports or self.
- `Employee`: own/created leads and follow-ups.

## In scope / pending

This is **Phase 1 starter**, not a production-complete CRM. Lead deletion, lead editing UI and automated integrations are not included yet. Meta Lead Ads, WhatsApp webhooks, bulk Excel import, reminders, attachments, notifications, audit logs, tests, deployment and production-grade browser session handling are pending. The frontend is a basic MVP; the backend has additional functionality available for UI expansion.

Do not deploy publicly until HTTPS, secrets management, testing, rate limits, monitoring, and secure cookie-based web auth have been reviewed.
