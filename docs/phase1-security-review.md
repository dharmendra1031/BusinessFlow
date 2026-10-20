# Phase 1 — Custom Role and Data Scope Security Review

**Date:** 20 October 2026  
**Target branch:** `development`  
**Verified application commit:** `4140d947d961787d85cfb04e206305e709b745e5`  
**Scope:** Source code, isolated SQL Server API tests, React build, Playwright Chromium, Windows IIS package checks. **This is not production penetration testing or deployed staging acceptance.**

## Reviewed controls

| Control | Evidence | Assessment |
| --- | --- | --- |
| JWT role and session validation | `Program.cs`, `TokenService.cs` | Authentication validates the active user, security stamp and current role memberships against token claims |
| Custom roles and deny-only restrictions | `RolePermissionService.cs`, `RoleAdministrationService.cs` | Custom grants are action-specific; user restrictions override grants, and protected Admin controls cannot be granted through custom roles |
| Scoped leads and projects | `RecordAccessService.cs` | Action-specific scopes filter queries; broad `leads.view` does not implicitly broaden `leads.edit`, dashboard or reports |
| Organization, team and hierarchy | `OrganizationService.cs`, `RecordAccessService.cs` | Self, direct reports, reporting subtree, team, department and organization scopes exist; team/dept membership and active status are checked |
| Sensitive employee lookup fields | `EmployeesController.cs` | **Hardened:** report lookups expose ID, name and active state only; assignment lookups expose the minimal employee fields required by current selectors |
| Admin-only identity and organization management | Controller `[Authorize(Roles = "Admin")]` policies | Admin users, role configuration, organization administration and employee mutations have additional role guards |

## Finding and fix

**Finding S1 — Unnecessary personnel-data disclosure through general-purpose lookup (fixed in this checkpoint).**

Before the change, `/api/employees/lookup?action=reports.view` reused the full employee directory serializer. A user with a wide but legitimately scoped reporting permission could receive unrelated personal fields such as phone number, manager/department identifiers and role membership, even though the Reports UI only needs an employee ID and display name. This was unnecessarily broad response data, not proof of arbitrary SQL access.

The response is now purpose-specific:

- `reports.view`: employee ID, full name and active state.
- Assignment actions: ID, name, email, active state and manager ID for existing project-management scoping.
- Employee directory/admin actions: retain the existing full response for authorized use.

The existing action-specific record-scoping queries, role checks, pagination and search remain in place. No SQL schema or UI-theme changes were needed.

## Regression and negative-access tests

Added `tests/BusinessFlow.Tests/Phase1ScopeSecurityTests.cs`:

1. A custom role with organization-wide report visibility may obtain names for report filtering, **but not** phone, email, role membership, department or manager details through the report lookup.
2. The same reader cannot access the full employee directory without `employees.view`, cannot expand its directory lookup scope without an explicit grant, and loses access to lookups immediately after an individual `employees.lookup` restriction.
3. For a Department-scoped custom role, only authorized colleagues' leads, projects and assignment candidates are accessible. After moving the employee between departments, the previous department's records disappear from its responses without requiring an application restart.
4. Cross-department record-by-ID requests return NotFound instead of another department's details.

### CI evidence

- [SQL/API + browser CI](https://github.com/dharmendra1031/BusinessFlow/actions/runs/39449608324) for `4140d94`: **38/38 SQL/API tests PASS**, **37/37 Playwright Chromium tests PASS**, EF migration snapshot, dependency audit and frontend production build PASS.
- [Windows IIS package](https://github.com/dharmendra1031/BusinessFlow/actions/runs/39449608325) for `4140d94`: **PASS**.
- Two prior temporary test-only CI failures arose from expecting Employee access to an `employees.view` protected endpoint. The correctly denied 403 and empty employee-view lookup results were then asserted explicitly; neither failure required lowering API permissions.

## Remaining security and release risks

- Multi-role combinations and cross-team/reporting-subtree exceptions need expanded adversarial scenario coverage, particularly where a built-in Manager created an item that was later assigned outside their current team. Existing Manager-created-record visibility is a backward-compatibility rule and **must not be silently changed** without business approval.
- UI controls that infer manageability using manager/owner IDs are not a substitute for server-authoritative per-record permission checks. Backend scoping remains the security boundary; a future capability endpoint may be useful.
- SQL backup and isolated full-restore rehearsal, existing-database migration on representative staging data, deployment security settings and real 100–200-concurrent-user load tests remain unverified.
- No Phase 1 production sign-off or live environment security certification is claimed.

**Next gate:** staging database backup/migration/restore verification followed by targeted complex multi-role and record-scope acceptance, then formal Phase 1 sign-off.
