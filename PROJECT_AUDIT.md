# Project audit and next steps

**Reviewed:** 19 October 2026  
**Branch:** `development` after fetching `origin/development` and rebasing the existing local deployment commit  
**Scope:** Repository code, README, deployment guide, migrations, automated tests, and CI configuration. This is a source review, not a production acceptance test.

## Current state

The application is a single-company CRM with a React/TypeScript frontend, ASP.NET Core 10 API, SQL Server, Identity roles, and an IIS packaging path. The latest GitHub code includes three development-delivery phases beyond the original lead CRM. The feature inventory below means **implemented in source**, not verified on a live server.

| Area | Implemented now | Evidence |
| --- | --- | --- |
| Identity and team | JWT login, Admin/Manager/Employee roles, employee creation and maintenance, deactivation, password change and admin reset | `AuthController`, `EmployeesController`, `AdminUsersController`, `PasswordsController` |
| Leads | Customer details and codes, search/filter/page, create/edit, assignment, status and assignment history, permission-scoped access | `LeadsController`, `LeadAccess`, `LeadDetails.tsx`, `pages.tsx` |
| Follow-ups | Schedule, edit, complete, overdue lists and lead history | `FollowUpsController`, `pages.tsx` |
| Dashboard and reports | Role-scoped summary, 10 report types, date/user/status/referrer filters, browser CSV export | `DashboardController`, `ReportsController`, `ReportsPage.tsx` |
| Development projects | Convert a Won lead once, set owner and dates, assign tasks, update progress/status, enforce completion rules | `ProjectsController`, `ProjectsPage.tsx` |
| Bugs and worklogs | Project bugs with severity/assignee/status; daily self-entered worklogs with a 24-hour daily limit and audit entries | `DevelopmentActivityController`, `ProjectActivity.tsx`, migration `20261019070000_AddDevelopmentActivity` |
| Planning | Milestones and sprints, task links, progress, date validation and completion gates | `ProjectPlanningController`, `ProjectPlanning.tsx`, migration `20261019100000_AddProjectPlanning` |
| Production foundation (Phase 4 source) | Secure cookie sessions, CSRF, production CSP headers, admin Audit history, per-role permission restrictions, structured error request IDs, SQL backup and HTTPS smoke scripts | `AuthController`, `Program.cs`, `AuditController`, `RolePermissionsController`, `scripts/` |
| Delivery | Additive EF migrations, Windows IIS publish script and deployment guide, two GitHub Actions workflows | `Migrations/`, `scripts/publish-windows.ps1`, `DEPLOY_WINDOWS.md`, `.github/workflows/` |

## Gaps and risks, in priority order

### P0 — Before production use

1. **Production deployment is not established as complete.** The guide describes a working IIS readiness check for an earlier build, but the current schema and features have no recorded end-to-end deployment, migration, backup/restore, HTTPS login, or rollback verification. Run a staging deployment with the exact release artifact and migration bundle, then record the checks and rollback result.
2. **Production cookie security implemented in source; acceptance pending.** Production sign-in issues an HttpOnly/Secure/SameSite=Strict cookie rather than returning a browser-readable token. Cookie-authenticated API writes require CSRF, and production responses set CSP/security headers. Development-only bearer compatibility remains. Verify deployed HTTPS cookies, session expiry, browser CSP, permission revocation and logout on staging before real customer data.
3. **Dependency advisories (patched in `development`; continue monitoring).** Earlier audit reported `System.Security.Cryptography.Pkcs` 9.0.4 (`NU1902`, `NU1903`). The backend now uses patched ASP.NET Core/EF packages 10.0.13 and explicitly references PKCS 10.0.13. Development CI now checks transitive NuGet advisories; do not assume future packages are safe without the check.
4. **Release CI migration script repaired in `development`; verify on merge.** The earlier `main` workflow expected `Data/Migrations` and generated a duplicate `InitialCreate`. The updated `verify.yml` applies committed migrations and verifies model snapshot consistency, without uploading a synthetic migration. The workflow is triggered on `main` or pull requests to `main`, so it still needs its own successful release/PR run after this branch is merged.

### P1 — Data completeness and daily operations

5. **Several lists silently stop at fixed limits.** Reports return at most 5,000 rows, project lists/eligible leads and follow-ups at most 200, and bugs/worklogs at most 500. Reports expose `truncated`, but CSV exports only the returned page. Add server pagination plus a complete, streamed export path; show truncation and totals clearly throughout the UI.
6. **Lead intake and reminders are manual.** No Meta Lead Ads or WhatsApp webhook ingestion, bulk Excel import, scheduled reminders, or notifications are implemented. Define deduplication, consent, retry/error handling, and ownership rules before adding integrations.
7. **Delivery artifacts and handoff are missing.** Projects have no file attachments, client approval/sign-off, release/deployment records, GitHub synchronization, or automated time capture. Choose the first two based on the team's actual delivery process; include access control, storage limits and audit history for files.
8. **Worklogs are append-only and the UI shows at most 500 entries.** This preserves history but leaves no correction/approval process for mistakes and can understate displayed totals. Add a manager-reviewed correction entry or approval workflow and aggregate totals from the database rather than loaded rows.
9. **Project planning is basic.** Sprints and milestones can be created and linked to tasks, but there is no dedicated board, backlog prioritization, capacity planning, dependencies, or cross-project workload view. Add these only after confirming the team's planning workflow.

### P2 — Quality and maintainability

10. **Automated coverage is uneven.** Browser tests cover major CRM and development flows, roles, forms, bugs, worklogs, and planning. The backend has one SQL Server integration test file. Add targeted tests for migration upgrades from an existing database, pagination/export, concurrent project completion/edits, and production auth controls. Run browser checks in the release pipeline, not only on `development`.
11. **Documentation needs ongoing release upkeep.** README now distinguishes Phase 1/2 historical scope from later implementations and documents Phase 4, but release status, environment-specific verification and the feature roadmap must be kept current.
12. **Accessibility and cross-browser validation are incomplete.** The UI has skip navigation, labels, keyboard-oriented dialogs and Chromium Playwright checks. Run a focused screen-reader/keyboard review and at least one non-Chromium browser pass for critical workflows.

## Recommended delivery sequence

1. Verify the updated `main` CI workflow on a pull request, then create a repeatable staging release check (migration, backup/restore, HTTPS, sign-in, rollback). Continue dependency scans on every commit.
2. Decide and implement the production session/security model; verify it with real deployment settings and a security review.
3. Add pagination and complete exports, then correct worklog totals/corrections.
4. Select the highest-value intake integration and delivery handoff features with stakeholders; specify permissions and data retention before implementation.
5. Expand targeted tests and update README after each release.

## Verification performed for this audit

- `git fetch origin --prune` succeeded. Local `development` was 36 commits behind and one local commit ahead; the local commit rebased cleanly onto latest `origin/development`.
- `npm run build` in `frontend` passed (TypeScript and Vite production build).
- `dotnet test tests/LeadManagement.Tests/LeadManagement.Tests.csproj --no-restore` compiled, then failed because `CRM_TEST_CONNECTION` was not configured locally. No local SQL Server integration test result is claimed.
- Browser tests and live deployment were not run in this audit. Their presence in source is recorded above; current CI run results were not inspected.

## 19 October 2026 remediation notes

The `development` branch now contains CI migration-path fixes, updated .NET 10.0.13 packages and an automated NuGet vulnerability gate. The development pipeline also runs the SQL Server permissions integration test and checks EF snapshot consistency. The original audit's production-readiness warnings still apply: latest Windows IIS ZIP, live HTTPS deployment, backup/rollback, auth storage and monitoring have not been accepted as complete.

## Phase 4 source implementation (not production acceptance)

The development branch includes production-cookie authentication, CSRF checks, an admin Audit history screen, manager/employee role permission restrictions, structured request diagnostics, a copy-only SQL backup helper and read-only HTTPS release verification script. See `PHASE4_PRODUCTION_CHECKLIST.md`. Full MFA/SSO, custom role creation, centralized monitoring, staged DB restore and live release verification remain pending.
