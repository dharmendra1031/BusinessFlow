# Enterprise Phase 1 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans for native execution, or superpowers:subagent-driven-development if selected by the user. Steps use checkbox syntax for tracking.

**Goal:** Implement and verify all ten Security & Organization items in ENTERPRISE_ROADMAP.md.

**Architecture:** Extend the existing Identity, EF Core and React application. Share action authorization and record scopes across controllers. Introduce additive organization, grants, restrictions, approvals and rowversion migrations, retaining built-in access defaults.

**Tech Stack:** ASP.NET Core 10, EF Core 10, SQL Server, React 19, TypeScript, xUnit and Playwright.

**Spec:** ../specs/2026-10-20-phase1-design.md

## Global Constraints

- New entity primary keys are BIGINT IDENTITY.
- Preserve existing CRM and development workflows and React styling.
- Production migration execution requires backup and staging upgrade evidence.
- Unavailable records return 404. Authenticated action denials return 403.
- User overrides are deny-only restrictions.
- Test behavior before implementation; SQL Server must prove transaction and rowversion behavior.
- Do not mark Verified or Accepted based only on a successful build.
- Implement in an isolated branch/worktree derived from development; keep main protected.

## Review Focus

- Multi-role users must not gain organization-wide scope for unrelated grants: task 3 couples scope to each role's grants.
- Removing a team member or revoking a grant must affect the next request, including exports: tasks 2–4 exercise revocation.
- Lead PUT/create must not bypass terminal-status approval: task 6 covers alternate write paths.
- Concurrent approvals/completion/worklogs must not create duplicate history or violate limits: tasks 5–7 use independent SQL sessions.
- Directory pagination must not break assignee selectors or expose unrelated employees: task 8 exercises every consumer.

## File responsibilities

Paths below are relative to the repository. Backend files use backend/BusinessFlow.Api/ as their root unless fully qualified.

- Models/Organization.cs and Contracts/OrganizationRequests.cs: department/team/membership entities and inputs.
- Services/OrganizationService.cs: transfer, disable and hierarchy invariants.
- Services/RecordAccessService.cs: query scopes for leads, projects and employees.
- Models/PermissionGrant.cs and Contracts/PermissionRequests.cs: Identity-backed role grants/scopes and per-user restrictions.
- Services/RolePermissionService.cs: effective action checks; extend existing implementation.
- Controllers/OrganizationController.cs and Controllers/RoleAdministrationController.cs: admin APIs.
- Services/ConcurrencyTokens.cs: token parsing and EF original-version application.
- Models/LeadStatusApproval.cs and Services/LeadApprovalService.cs: terminal transition workflow.
- Services/LoginAttemptLimiter.cs: bounded account/IP attempt tracking.
- frontend/src/OrganizationPage.tsx, RolesPage.tsx, LeadApprovals.tsx: focused administration/workflow components.
- tests/BusinessFlow.Tests/Phase1*: SQL and policy verification; frontend/e2e/phase1.spec.ts: browser verification.

## Task 1: Establish reproducible baseline

**Files:** tests/BusinessFlow.Tests/Phase1TestDatabase.cs; docs/phase1-verification.md.

**Interfaces:** Produce a disposable fixture exposing Task<AppDbContext> CreateContextAsync() and an isolated LeadManagement_CITests_Phase1 database, never a production catalog.

- [ ] Read using-git-worktrees skill and create isolated branch/worktree; carry approved design and plan into it.
- [ ] Establish process-scoped SQL connection using localhost integrated authentication; do not persist credentials. Run the current backend suite and browser regression against dedicated test databases; record actual results.
- [ ] Implement shared test fixture with separate contexts/connections and fixture-owned data cleanup. Add real endpoint hosting if controller-only tests cannot exercise middleware.
- [ ] Record baseline commit, environment and commands in docs/phase1-verification.md; commit task changes.

## Task 2: Departments, teams and manager hierarchy

**Files:** Models/Organization.cs; Models/Entities.cs; Data/AppDbContext.cs; Contracts/OrganizationRequests.cs; Services/OrganizationService.cs; Controllers/OrganizationController.cs; Controllers/AdminUsersController.cs; additive migration; frontend/src/OrganizationPage.tsx and App.tsx; tests/BusinessFlow.Tests/Phase1OrganizationTests.cs.

**Interfaces:** OrganizationService.TransferAsync(long userId, long? departmentId, long[] teamIds, CancellationToken ct); SetManagerAsync(long userId, long? managerId, CancellationToken ct). Entities expose numeric Id, Name and IsActive; team exposes DepartmentId, membership exposes TeamId/UserId. Enforce unique team/user membership.

- [ ] Write tests: transfer to department B removes explicitly deselected A memberships atomically; invalid destination leaves all original memberships; self/cyclic/inactive-manager assignment fails; disabling populated organization units fails; admin writes create audit records and non-admin calls return 403.
- [ ] Run dotnet test --filter FullyQualifiedName~Phase1OrganizationTests; observe failures for missing behavior.
- [ ] Add entities, additive migration and service/controller operations; route existing manager edits through hierarchy validation. Reject inconsistent membership/department combinations.
- [ ] Add admin organization page with create/edit/deactivate, membership and transfer forms using current CSS/components. Add browser assertions for valid transfer and actionable validation errors.
- [ ] Rerun targeted SQL/browser cases; commit after green.

## Task 3: Custom roles and deny-only individual restrictions

**Files:** Models/PermissionGrant.cs; Services/RolePermissionService.cs; Controllers/RoleAdministrationController.cs; Controllers/RolePermissionsController.cs; Contracts/PermissionRequests.cs; Data/AppDbContext.cs; additive migration; frontend/src/RolesPage.tsx and PermissionsPage.tsx; tests/BusinessFlow.Tests/Phase1PermissionTests.cs.

**Interfaces:** AllowedAsync(ClaimsPrincipal principal, string permission) remains Task<bool>. EffectiveAsync remains Task<Dictionary<string,bool>>. Role grants reference Identity role IDs, action code and scope; user restriction references UserId/action code. Unknown actions deny. Scope enum: Self, DirectReports, ReportingSubtree, Team, Department, Organization.

- [ ] Write tests proving new custom role grants nothing, explicit grant enables one action, user deny defeats multiple role grants, deny removal restores baseline, and edits affect the next request. Reject unknown actions and attempted user grants.
- [ ] Write protection tests for Admin rename/delete/reduction, ordinary employee edits granting Admin, and simultaneous attempts to remove/disable the last active Admin.
- [ ] Observe targeted failures, then implement role/grant/restriction APIs and atomic bootstrap protections; seed compatibility defaults without changing existing numeric IDs.
- [ ] Extend permission/role UI and employee-specific restrictions; audit every effective change.
- [ ] Run targeted tests and UI cases; commit after green.

## Task 4: Central action and record scope enforcement

**Files:** Services/RecordAccessService.cs; Services/RolePermissionService.cs; Program.cs; all existing Controllers using record access; Services/LeadAccess.cs; frontend/src/App.tsx, pages.tsx, ProjectsPage.tsx, ProjectActivity.tsx, ProjectPlanning.tsx, ReportsPage.tsx; tests/BusinessFlow.Tests/Phase1AccessTests.cs.

**Interfaces:** RecordAccessService.LeadsAsync(ClaimsPrincipal user, string action, CancellationToken ct) -> Task<IQueryable<Lead>>; ProjectsAsync -> Task<IQueryable<DevelopmentProject>>; EmployeesAsync -> Task<IQueryable<ApplicationUser>>. Query builders take the action so a wider scope on another grant cannot leak records.

- [ ] Add endpoint tests over two departments, multiple teams and three reporting levels: self/direct/subtree/team/department/organization scopes, assigned project access and inactive membership revocation. Assert forbidden actions=403, unrelated record reads/writes=404.
- [ ] Cover dashboard aggregates, history, followups, reports/CSV/PDF, eligible leads, task/bug/worklog/planning endpoints and assignee lists. Pin a multi-role user with narrow edit and wide view to narrow edit scope.
- [ ] Observe failures, implement cycle-safe reporting traversal and shared query scopes; replace duplicated project access logic and fixed role checks where custom role grants apply. Keep organization/role administration Admin-only.
- [ ] Replace module-prefix-only checks with action mapping, preserving existing module restrictions as parent restrictions. Update UI action controls from effective permissions and record capability data.
- [ ] Run access matrix and baseline permission tests; commit after green.

## Task 5: Optimistic concurrency and project completion

**Files:** Models/Entities.cs; Models/ProjectEntities.cs; Data/AppDbContext.cs; Services/ConcurrencyTokens.cs; Contracts/Requests.cs and Projects.cs; LeadsController.cs; ProjectsController.cs; frontend/src/types.ts, api.ts, pages.tsx, LeadDetails.tsx, ProjectsPage.tsx; tests/BusinessFlow.Tests/Phase1ConcurrencyTests.cs; additive migration.

**Interfaces:** Lead/project/task DTOs expose rowVersion as base64. Existing-record writes require rowVersion, including assignment/status. ConcurrencyTokens.TryParse(string? token, out byte[] version) validates SQL Server 8-byte tokens; EF original value is set before saving. Missing/malformed versions return 400; stale versions return 409.

- [ ] Write SQL tests with independent contexts: stale lead edit/assign/status and stale project/task update return 409 with no extra audit/history; missing/malformed tokens return 400. Concurrent task creation/status change cannot invalidate an accepted project completion.
- [ ] Observe failures, generate rowversion migration and apply original-value checks to every relevant mutation. Protect completion reads and task mutations through compatible transactional locking; do not rely solely on the project's rowversion for child changes.
- [ ] Return updated tokens or reload records after writes. Preserve unsaved frontend fields on conflict and show reload action. Update existing browser/API fixtures to send versions.
- [ ] Run concurrency tests, EF snapshot check and frontend build; commit after green.

## Task 6: Won/Lost approval workflow

**Files:** Models/LeadStatusApproval.cs; Contracts/ApprovalRequests.cs; Services/LeadApprovalService.cs; Controllers/LeadApprovalsController.cs; Controllers/LeadsController.cs; Data/AppDbContext.cs; additive migration; frontend/src/LeadApprovals.tsx, LeadDetails.tsx, pages.tsx; tests/BusinessFlow.Tests/Phase1ApprovalTests.cs.

**Interfaces:** RequestAsync(long leadId, long statusId, byte[] version, ClaimsPrincipal user, CancellationToken ct); DecideAsync(long approvalId, bool approve, string? comment, ClaimsPrincipal user, CancellationToken ct); OverrideAsync(long leadId, long statusId, byte[] version, string reason, ClaimsPrincipal user, CancellationToken ct). Return typed outcomes mapped to HTTP status, never bypass action/scope checks.

- [ ] Write tests: request leaves status unchanged; another scoped approver commits one history entry; reject leaves status unchanged; requester cannot approve; duplicate pending request conflicts; stale version conflicts; parallel decisions commit once; override requires Admin and nonblank reason.
- [ ] Test terminal status supplied through lead creation/PUT/PATCH cannot bypass approval; existing Won conversion stays available; ordinary nonterminal changes retain current behavior with version checks.
- [ ] Observe failures, implement atomic request/decision/override plus audit, unique pending protection and stale-request checks. Approval records retain captured lead version and actor/time/decision history.
- [ ] Add pending state, approve/reject and reasoned Admin override UI. Update affected baseline tests to follow the approved policy.
- [ ] Run targeted SQL and browser scenarios; commit after green.

## Task 7: Office login protection and safe concurrent worklogs

**Files:** Services/LoginAttemptLimiter.cs; Program.cs; Controllers/AuthController.cs; Controllers/DevelopmentActivityController.cs; appsettings.json; tests/BusinessFlow.Tests/Phase1LoginTests.cs and Phase1WorklogTests.cs.

**Interfaces:** LoginAttemptLimiter.CheckAsync(string normalizedAccount, string remoteIp, CancellationToken ct) -> Task<bool>; use TimeProvider for deterministic expiry tests. Defaults: account 5 attempts/minute, aggregate IP 300 attempts/minute; preserve Identity five-failure/15-minute lockout. Cap retained account/IP keys and expire idle entries; return 429 when capacity is exhausted rather than evicting active protection.

- [ ] Write tests: 100 different valid accounts behind one IP can log in; repeated one-account attempts across IPs are bounded; nonexistent accounts and casing/whitespace variants share protection; window expiry frees capacity; concurrent attempts cannot exceed configured limits; spoofed forwarded headers do not change identity.
- [ ] Observe failures, implement bounded atomic account/IP accounting before password verification, generic errors and validated configuration. Retain lockout and current cookie/CSRF behavior.
- [ ] Write independent-SQL-connection worklog tests posting across projects so attempted sum exceeds 24; assert persisted sum <=24, accepted rows match successes and rejected rows have no audit. Pin deadlock/serialization response as retryable 409.
- [ ] Preserve Serializable validation and add conflict mapping or bounded whole-transaction retry; run concurrent SQL tests repeatedly enough to exercise overlap, then cookie/CSRF regression tests. Commit after green.

## Task 8: Employee directory and bounded lookups

**Files:** Controllers/EmployeesController.cs; Contracts/EmployeeDirectoryRequests.cs; frontend/src/pages.tsx, EmployeeManager.tsx, ProjectsPage.tsx, ProjectActivity.tsx, ReportsPage.tsx; tests/BusinessFlow.Tests/Phase1DirectoryTests.cs; frontend/e2e/phase1.spec.ts.

**Interfaces:** GET /api/employees accepts search, role, isActive, managerId, departmentId, teamId, page=1 and pageSize=20 (1–200); returns {total,page,pageSize,items}. Order by FullName then Id. GET /api/employees/lookup accepts search, page and pageSize and returns the same envelope with minimal id/fullName fields, scoped to permitted assignees.

- [ ] Write tests with >200 employees and duplicate names proving distinct stable pages, combined filters, invalid pagination, totals and no unrelated employee leakage. Check inactive employee behavior separately for directory and assignments.
- [ ] Observe failures, implement scoped SQL filtering/count/paging; avoid full employee loads for role lookup.
- [ ] Update every employee-list consumer to directory or lookup envelopes and add search/page controls. Verify lead/project/bug assignments, report user filters and employee creation/edit still work.
- [ ] Run targeted SQL/browser cases and frontend build; commit after green.

## Task 9: Phase gate and evidence

**Files:** tests/BusinessFlow.Tests/Phase1MigrationTests.cs; frontend/e2e/phase1.spec.ts; docs/phase1-verification.md; ENTERPRISE_ROADMAP.md.

- [ ] Rehearse upgrade from baseline schema populated with existing roles/users/leads/projects and fresh migration; assert preserved IDs, intended baseline grants, valid memberships and populated rowversions. Verify EF migration snapshot consistency.
- [ ] Run full dotnet test tests/BusinessFlow.Tests/BusinessFlow.Tests.csproj and npm run build plus npm run test:e2e against isolated SQL databases, including full HTTP authorization tests. Investigate and record every failure.
- [ ] Run new admin, custom-role, approval and conflict UI flows with keyboard/mobile checks; inspect relevant screens for theme regressions. Ensure API denials remain effective when UI controls are bypassed.
- [ ] Review all ten roadmap acceptance requirements against test evidence. Record exact commands, environment, commits and remaining acceptance gaps. Mark only proven states; keep production deployment acceptance separate.
- [ ] Request code review using the applicable skill, resolve findings and rerun affected tests. Commit evidence and present final diff/test results. Do not merge main or deploy implicitly.

## Plan self-review

All ten roadmap items map to tasks 2–8. The Phase gate maps to task 9. Review-focus failures map to explicit tests above. Existing SQL Serializable worklog protection is retained and tested, not reported as missing. Execution method and plan review are pending.
