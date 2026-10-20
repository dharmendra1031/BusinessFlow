# Enterprise Phase 1 — proposed design

Status: Approved by the user on 20 October 2026, including the Won/Lost approval policy. Covers all ten Phase 1 roadmap items.

## Purpose and constraints

Support one company with 100–200 employees while preserving the existing CRM and development workflows, React styling, ASP.NET Core/Identity services and SQL Server database. New entity primary keys are BIGINT IDENTITY. Schema changes are additive migrations; production migration execution requires backup and staging upgrade evidence.

## Approach

Extend the current application with focused organization, authorization, approval and directory services. Centralize scope and action checks shared by controllers; do not duplicate project access rules across planning and activity controllers. Keep built-in Admin, Manager and Employee roles and their existing baseline behavior while introducing Identity-backed custom roles. A separate authorization system or application rewrite would increase migration and regression risk.

## Organization

Add departments, teams and team memberships. An employee has one optional department and may belong to multiple teams. A team belongs to one department. Membership changes require active employees, departments and teams. Transfers must explicitly move or remove incompatible memberships in one transaction. Disable operations reject active dependent teams/memberships until the administrator transfers or deactivates those relationships. Reporting-manager changes reject self-reference, inactive managers and cycles.

Admin screens manage departments, teams and memberships using existing form/table patterns. All changes are audited.

## Roles and permissions

Use existing numeric Identity role IDs. Custom roles have permission grants and explicit data scopes; newly created roles grant nothing. Retain built-in baseline restrictions during migration. Protect Admin from rename, deletion or permission reduction, and prevent removal/deactivation of the last active administrator. Ordinary employee editing must not grant Admin.

Define action permissions for lead read/create/edit/assign/status/request/approve, project read/create/edit, task create/edit/status, bug create/edit/status/close, worklog read/post, planning read/manage, report read/export/PDF and employee read/create/edit/disable/password-reset. Add organization and role administration permissions limited to Admin initially.

Require the action permission and matching record scope at every API, including related detail/history, exports, dashboard aggregates and assignee lists. Unavailable records return 404. Authenticated action denials return 403. Frontend controls reflect the same effective permissions.

User overrides are deny-only restrictions: no override may grant beyond the union of authorized role grants. Evaluate current grants/restrictions from the database so revocation takes effect on the next request. Audit role, scope and restriction changes.

## Scope

Support self, direct reports, reporting subtree, team, department and organization scopes. Admin retains organization scope. Existing Manager defaults remain self/direct reports; Employee defaults remain self plus assigned delivery work. Custom roles default to self and need explicit scope configuration. Reporting traversal must be cycle-safe. Inactive membership gives no access. Team/department affiliation alone never grants a module/action.

Phase 1 assigned-project access preserves owner, task-assignee and bug-assignee relationships. A dedicated project-membership module remains Phase 3.

## Approved approval policy

Employee/Manager requests to mark a lead Won or Lost create a pending approval without changing the effective lead status. A different authorized approver within the record scope approves or rejects with an optional comment. Admin may execute an explicit, audited override with a required reason. Pending requests capture the original lead version; stale requests cannot be approved. Duplicate pending terminal transitions are rejected. Approval and status history are committed atomically. Lead edits cannot bypass the workflow through the create/update status fields. Existing Won leads remain Won, and conversion continues to require an effective Won status.

Other statuses retain their current behavior. UI adds request state and approval actions to the lead workflow.

## Authentication and shared office IPs

Replace the five-per-IP login bottleneck with independent account-aware attempt protection and a higher aggregate IP abuse ceiling. Preserve Identity failed-attempt lockout and generic credential errors. Apply limits to nonexistent accounts too, normalize account keys consistently and avoid unbounded limiter retention. Test many different accounts behind one IP, one attacked account across multiple IPs, invalid input and recovery after the configured window. Do not trust forwarded client IP headers without an explicitly configured proxy.

## Optimistic concurrency

Add SQL Server rowversion columns to leads, projects and tasks. Return opaque base64 versions and require the version for writes to existing records, including assignment/status and terminal transitions. Use EF original-value checks and map stale writes to 409 with a clear reload message. Reject malformed/missing versions consistently. The UI retains unsaved input and asks the user to reload rather than silently overwriting. Project completion uses a transaction that also protects its related completion conditions against concurrent task mutations.

## Worklog safety

Preserve the existing Serializable transaction and SQL aggregate daily limit. Verify simultaneous posts using independent SQL connections; handle serialization/deadlock conflicts with a bounded retry or a clear retryable conflict. After every accepted batch, the persisted daily sum must be at most 24 hours across all projects. Failed attempts must leave no worklog or audit entry. Keep database-computed totals and pagination.

## Employee directory

Add server-side name/email search, role/active/manager/department/team filters and deterministic pagination with total counts. Separate bounded assignee lookups from the admin directory so existing lead/project forms remain usable. Update every frontend consumer to the new response shape. Prevent restricted employees and unrelated teams from appearing in lookups.

## Verification and release evidence

For each item write behavior tests before implementation. Run negative access tests across two departments, multiple teams, nested managers, custom roles and user restrictions. Cover direct HTTP calls, exports and related-resource endpoints. Test bootstrap protections, cyclic hierarchies, disable/transfer transactions, stale writes, simultaneous worklogs, approval races, terminal-status bypass and directory paging.

Use SQL Server for migration, rowversion and transactional evidence; an in-memory provider is insufficient. Rehearse a fresh database and an upgrade from the current schema with existing users/leads/projects, retaining their numeric IDs and baseline access. Run backend tests, frontend build, browser regression and new admin/approval/conflict flows. Record commands, results, environment, commit and remaining gaps in the roadmap. A build alone does not mark a work item Verified or Accepted. No production deployment or production database migration is implied by this implementation task.

## Implementation sequence

1. Establish SQL/browser test baseline and isolated development branch/worktree.
2. Organization schema, hierarchy validation and administration UI.
3. Role grants, restrictions, centralized action/scope enforcement and permission UI.
4. Rowversion migration, API contracts and conflict UI.
5. Terminal-status approval flow using the agreed business policy.
6. Account-aware throttling and concurrent worklog verification.
7. Employee directory filters/pagination and all lookup consumers.
8. Fresh/existing migration rehearsals, full regression, permission matrix and evidence updates.
