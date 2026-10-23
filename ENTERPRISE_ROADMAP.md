# BusinessFlow — 100+ Employee Enterprise Roadmap

**Baseline date:** 19 October 2026  
**Target:** Single-company IT/software services organization with 100–200 employees  
**Working branch:** `development` (keep `main` protected)  
**Status:** Audit and planning only. No enterprise backlog item below is accepted as complete without implementation and test evidence.

This roadmap extends the existing BusinessFlow CRM and software-delivery system. It does not replace the existing project architecture. Read it with [PROJECT_AUDIT.md](PROJECT_AUDIT.md), [PHASE4_PRODUCTION_CHECKLIST.md](PHASE4_PRODUCTION_CHECKLIST.md), and [RELEASE_RECORD.md](RELEASE_RECORD.md).

## Engineering rules

- Preserve existing user flows, business behavior, UI theme and colors unless an approved change requires otherwise.
- Make focused, minimal changes. Reuse existing ASP.NET Core services, React components, SQL Server schema and migrations.
- All application/database entity primary keys use numeric auto-increment `BIGINT IDENTITY`; no UUID/GUID entity IDs.
- Use descriptive, conventional names and small, maintainable implementations; avoid duplicated permission logic or excessive CSS overrides.
- Additive, reviewed SQL Server migrations only. Take a database backup and test upgrades on staging before applying to production.
- Enforce permissions on the API and query/data scope; UI visibility is not a security boundary.
- Track every item as **Not started**, **In progress**, **Implemented**, **Verified**, or **Accepted**. A successful CI build is not production acceptance.
- Record exact test evidence, commit, environment, remaining risks and release acceptance for every delivered phase.

## Baseline: already present in source

| Area | Source-level state | Production acceptance |
| --- | --- | --- |
| Sign-in, employee creation/edit/deactivation, password change/reset | Implemented | Pending |
| Admin, Manager, Employee identity roles and restricted permission overrides | Basic implementation | Pending |
| Leads, statuses, assignments, assignment history, follow-ups | Implemented | Pending |
| Lead sources (Manual, Website, Facebook, WhatsApp) | Seeded lookup only | Automated intake not implemented |
| Dashboard, 10 report types, pagination, CSV, RDLC PDF pipeline | Implemented | Deployment/output review pending |
| Won lead conversion to one development project | Implemented | Pending |
| Projects, tasks, bugs, worklogs, sprints and milestones | Implemented | Pending |
| Admin audit history, secure production-cookie/CSRF code, IIS release scripts | Implemented | Staging/live checks pending |
| 100–200 concurrent users | Not load tested | Pending |

Source evidence: `backend/BusinessFlow.Api/`, `frontend/src/`, `tests/BusinessFlow.Tests/`, `frontend/e2e/` and GitHub Actions workflows. The 19 October 2026 `development` build (`b1b0c569`) passed the development UI/SQL CI and Windows staging-package workflows. Those results do not certify a deployed Windows site or concurrency capacity.

## Confirmed design gaps and risks

### Authorization and organization

1. Three fixed roles are insufficient for Sales, HR, Finance, Project Managers, Team Leads, Developers and QA. The existing `RolePermissionService` exposes ten permission codes, mainly at module level.
2. `projects.view` is mapped at the route-prefix level, so separate project create/edit/task/bug/worklog permissions are needed in addition to existing business checks.
3. Lead and project scopes use the current user and direct manager relationships. Department, team and full management hierarchy scopes are missing.
4. The existing permission overrides disable baseline Manager/Employee abilities by **role**, not per employee. The feature must retain least-privilege behavior while adding custom roles and user-specific restrictions.
5. Lead Won/Lost status changes lack an approval flow. Decide approvals and exceptions with business owners before enforcement.

### Data integrity and scale

6. Lead phone/email deduplication and inbound lead idempotency are missing; manual lead creation should not silently overwrite or discard a potential duplicate.
7. Leads, projects and development tasks do not have domain-specific optimistic concurrency tokens. Concurrent edits may overwrite changes; add and test conflict handling.
8. Developer worklogs validate the 24-hour per-day limit using a read-before-write total; simultaneous inserts require transactional concurrency protection.
9. Employee lists return an unpaginated directory. The dashboard's `userWise` aggregation currently uses `Take(100)`.
10. Dashboard follow-up "today" uses UTC; decide company timezone and consistently calculate business-day boundaries.
11. Lead `ProjectAmount` is not a signed quotation or recognized revenue record. Keep proposals, contracts and invoices distinct from pipeline value.
12. KWD is hardcoded in several UI labels; prefer a company-level currency setting if required by the business.

### Integrations and operations

13. Lead source labels do not mean the leads arrive automatically. Meta, WhatsApp, website forms, Google Ads, LinkedIn, email and marketplace connections need verified provider-side implementation, consent, secrets, retries and webhook signature checks.
14. Reminders, escalation, activity timelines, client attachments, UAT sign-off and release/deployment records are not complete.
15. The current default login rate limit is five requests/minute per remote IP. Test office NAT/shared-IP scenarios and adopt account-aware protection without weakening abuse defenses.
16. Production staging, monitored backup/restore, centralized alerts, security review and 100–200 concurrent-user performance acceptance remain outstanding.

## Phase 1 — Security & Organization

**Priority:** P0; implement before widening access.

| Work item | Acceptance requirement | State |
| --- | --- | --- |
| Departments and teams | Admin can manage active departments, teams, memberships; safe transfer/disable behavior | Verified |
| Custom roles | Configurable business roles using existing Identity role IDs; preserve bootstrap Admin protections | Verified |
| Fine-grained permissions | API-enforced action permissions for leads, projects, tasks, bugs, worklogs, reports and employee actions | In progress |
| Individual restrictions | User-level permission overrides cannot grant access beyond authorized policy; changes audited | Verified |
| Hierarchy and data scope | Self, direct team, reporting subtree, assigned project and organization scope explicitly defined and tested | Implemented |
| Approval gates | Define and implement authorized Won/Lost/other critical transitions; preserve history | Verified |
| Login throttling | Shared office-IP scenarios tested; abuse and lockout controls retained | Verified |
| Optimistic concurrency | Row-version checks for lead, project and task changes with clear HTTP conflict behavior | Verified |
| Worklog posting safety | Parallel requests cannot exceed daily time limit; test transaction isolation | Verified |
| Employee directory scale | Server-side search/filter/pagination; UI uses returned totals | Verified |

**Checkpoint (20 October 2026):** Source implementation covers all ten work areas. Eight items passed targeted SQL/API and browser CI checks, hierarchy/data scope is implemented, and full action-based UI and scope review remains in progress. No item is production-accepted.

**Recovery checkpoint (21 October 2026):** GitHub's disposable SQL Server backup/restore test restored a pre-Phase-1 schema into a separate randomized catalog, applied current migrations, preserved sample data/IDs and verified the original catalog remained unchanged. [39/39 SQL/API and 37/37 Playwright CI](https://github.com/dharmendra1031/BusinessFlow/actions/runs/39478020929); [Windows IIS package PASS](https://github.com/dharmendra1031/BusinessFlow/actions/runs/39478020925). **Actual VPS/customer staging backup and restore are still pending.** See [staging recovery runbook](docs/phase1-staging-recovery.md).

**Phase gate:** Role matrix and cross-department negative-access tests; migrations and existing data upgrade; concurrency tests; existing CRM and project regression tests. Remaining: exact per-record capabilities for complex custom-role scopes, final code/security review, staging migration, backup/restore and release acceptance. No unauthorized data access.

## Phase 2 — Enterprise CRM

| Work item | Acceptance requirement | State |
| --- | --- | --- |
| Lead source master | Admin can add, rename and deactivate a source without disrupting historical leads | CI Verified; staging pending |
| Duplicate review | Normalized phone/email matching, manual Admin pair review; no automatic merge, historical ownership preserved | CI Verified (review MVP); merge policy pending |
| Capture integrations | Authenticated website/Meta/WhatsApp/other provider intake only after approved credentials and data handling rules | Website signed intake CI Verified; Meta/WhatsApp pending |
| Integration resilience | Verified signatures, idempotency, bounded retries, failure/audit logs and secure secret storage | Website HMAC, receipt idempotency and rate limit CI Verified; provider retry/monitoring pending |
| Lead routing | Rules for team capacity, source, territory and manual manager override; assignment history remains accurate | Website source-to-team least-load + capacity CI Verified; territory and advanced routing pending |
| Follow-up reminders | Scheduled notifications, retries, ownership changes and opt-out/consent | In-app scoped due/overdue reminders implemented; external delivery/retries/opt-out pending, staging pending |
| Source reporting | Lead count, qualification, conversion and attributed value by source and selected period | Not started |
| Proposals | Versions, approvals, client response and handoff to delivery | Not started |

**Phase gate:** End-to-end lead capture → duplicate review → assignment → follow-up → Won → project; permission and replay tests, provider sandbox evidence where applicable.

## Phase 3 — Project Delivery

| Work item | Acceptance requirement | State |
| --- | --- | --- |
| Explicit project teams | Membership and role-controlled project scope, not merely task/bug assignment | Not started |
| Capacity and workload | Assigned effort versus availability; filters for team, sprint, project and period | Not started |
| Timesheet approval | Review, correction entries and approval history without destroying original logs | Not started |
| QA management | Test cases, execution cycles, bug lifecycle and defined release gates | Not started |
| Documents | Protected SRS, client files, attachments, versioning, size limits and retention | Not started |
| Client handover | UAT evidence, acceptance/sign-off, owner and date | Not started |
| Releases | Version, deployment destination, approval, rollback information and audit trail | Not started |
| Support | Tickets, assignees, priorities and SLA/AMC only after business scope is agreed | Not started |

**Phase gate:** Project team authorization, complete delivery history and clean handoff from a Won lead through acceptance.

## Phase 4 — Production Readiness & 100–200 User Acceptance

| Work item | Acceptance requirement | State |
| --- | --- | --- |
| Load/stress/soak tests | 25, 100 and 200 concurrent users with documented workload, latency, failure rate and server/SQL telemetry | Not started |
| Database performance | Query plans and indexes reviewed for realistic lead/project/report data volumes; no N+1 or uncontrolled list growth | Not started |
| Release candidate | Identified commit, reproducible Windows IIS artifact and migration bundle checksums | Not accepted |
| Staging migration | Fresh and existing-database upgrade verified using a representative sanitized copy | Not started |
| Production security | HTTPS, cookie/CSRF, permissions, security headers, secret storage, session revocation and audit verification | Not accepted |
| Backup and restore | Off-server backup, isolated restore, integrity checks, recovery-time evidence and rollback rehearsal | Not started |
| Monitoring | Application errors, SQL performance, backup failures and alert receipt verified | Not started |
| Cross-browser/accessibility | Critical keyboard, mobile and non-Chromium flows checked | Not started |
| Live release acceptance | Sign-off using `RELEASE_RECORD.md`; no "production-ready" label beforehand | Not accepted |

**Phase gate:** Evidence-backed sign-off for the target environment, realistic 100–200 user workload, recovery, support ownership and rollback plan.

## Optional company-wide modules

HRMS/attendance/leave/payroll, accounting/invoicing/payments, and asset management are **not part of the core CRM + software-delivery scope**. Add them only if BusinessFlow is explicitly intended to become the company's complete business management suite. Being a 100-person company alone does not require all of these modules.

## Proposed database work (not applied)

- `Departments`, `Teams` and a user-team membership mapping.
- Identity-backed custom roles and permission mappings; `UserPermissionOverrides`.
- Extend `LeadSources` for administration and add an inbound `LeadIntegrationEvents` log if integrations are approved.
- `ProjectMembers` for explicit team scoping.
- Add SQL Server `rowversion` concurrency tokens to `Leads`, `DevelopmentProjects` and `DevelopmentTasks`.
- Add only the smallest additional tables necessary for approved proposals, approvals, notifications, QA, documents and releases.

Any new entity primary key is numeric auto-increment `BIGINT IDENTITY`. All changes require additive EF Core migrations and an existing-database upgrade rehearsal. No database changes were made in this roadmap commit.

## Delivery tracking template

For each approved task, record:

- **Work item / phase / owner**
- **Before and after behavior**
- **Files and schema touched**
- **Migration and rollback impact**
- **Unit, SQL integration, browser and security results**
- **CI run and commit reference**
- **Current state:** Not started / In progress / Implemented / Verified / Accepted
- **Remaining gap or blocker**

Start with **Phase 1**. Do not start or claim completion of later enterprise phases merely because the original repository already uses the phrase "Phase 4" for its earlier production-foundation work.
