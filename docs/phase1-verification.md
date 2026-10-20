# Phase 1 verification evidence

## Baseline — 20 October 2026

- Branch: development; source baseline ce3cd8e. Design approved; implementation plan awaiting user review.
- Environment: local Windows, .NET 10, SQL Server 15.0.2200.2 via integrated authentication.
- Isolated test catalog: LeadManagement_CITests_Phase1Baseline_20261020. No production database was used.
- Command: `dotnet test tests/BusinessFlow.Tests/BusinessFlow.Tests.csproj --no-restore --logger 'console;verbosity=minimal'`, with process-scoped `CRM_TEST_CONNECTION` targeting that catalog.
- Result: PASS, 1 passed, 0 failed, 0 skipped. Existing test migrated the dedicated database and exercised its permissions/integration assertions.
- Limit: this is the existing single SQL integration test; it does not verify new Phase 1 requirements, full HTTP middleware, upgrade rehearsal or concurrent requests.
- Earlier frontend production build and API build passed in this session. They do not establish Phase 1 completion.

## Implementation checkpoint — 20 October 2026

Implemented departments/teams/membership transfers, manager-cycle validation, custom Identity roles, scoped grants, deny-only employee restrictions and shared backend action/scope checks. Admin organization and custom-role screens are included. Two additive migrations introduce organization and permission records with BIGINT IDENTITY keys.

- Latest full backend command: `dotnet test tests/BusinessFlow.Tests/BusinessFlow.Tests.csproj --no-restore --logger 'console;verbosity=minimal'`: 22 passed, 0 failed, 0 skipped on isolated SQL test databases.
- `npm run build`: passed.
- Full Chromium regression: 29 passed before the final employee-ownership permission correction; that correction has three HTTP regression cases, all reproduced failing before the fix and passing afterward in the full 22-test SQL/API run.
- EF `migrations has-pending-model-changes`: no pending model changes. `git diff --check`: clean.
- Independent code review identified employee project ownership incorrectly conferring management access. Fixed by excluding Employee baseline grants from management scopes; explicit custom-role grants remain supported. Tests cover colleague tasks, bugs and worklogs.
- Review observation retained: inactive employees must be reactivated before membership transfer, following the approved specification. This can add administration steps.

## Outstanding

Phase 1 remains in progress. Full action-capability UI, additional directory/lookup controls, broader permission and concurrency acceptance and final review remain pending. This checkpoint is not Phase 1 completion or production acceptance.

## Second implementation checkpoint — 20 October 2026

- Lead, project and task models now have SQL Server rowversions. Existing-record edit/status/assignment contracts require an 8-byte base64 version; malformed/missing versions return 400 and stale writes return 409. Editors preserve input and provide explicit reload. Successful lead edits return the new version in `X-Row-Version`.
- Won/Lost requests preserve the effective status until a different scoped approver decides. Admin overrides require a reason. Create/update/status paths enforce the terminal-status policy, and overrides close pending requests with a recorded explanation. Approval UI, history and scoped review queue are present.
- Account/IP login protection replaces the five-per-IP office bottleneck: account default 5/minute, IP default 300/minute, tracked-key default 10,000. Legacy `LoginRequestsPerMinute` remains an account-limit configuration alias. Identity lockout remains enabled. State is local to the running application process.
- SQL tests post worklogs concurrently across projects using independent HTTP request scopes; persisted total stays at or below 24 hours and audit records match accepted rows.
- Employee directory supports deterministic pagination, totals, search and role/active/manager/department/team filters. Assignment consumers use action-scoped active-employee lookups. Further lookup search controls and full UI capability enforcement remain outstanding.
- Shared feedback now shows success/error dialogs and delayed API loading, including keyboard close/focus restoration. Dedicated Chromium coverage verifies loading, success, server errors and preserved form input. The regression fixture explicitly dismisses success dialogs; dedicated concurrency tests deliberately retain stale versions.
- Fresh Release backend command: `dotnet test tests/BusinessFlow.Tests/BusinessFlow.Tests.csproj -c Release --no-restore --logger 'console;verbosity=minimal'`: **36 passed, 0 failed, 0 skipped** on isolated SQL databases.
- Full Chromium command: `npx playwright test --reporter=list`: **32 passed** against `LeadManagement_CITests_Phase1Browser`. This includes the employee request/admin approval journey and a real concurrent lead edit/reload flow.
- `npm run build`: passed. EF `migrations has-pending-model-changes`: no pending changes. `git diff --check`: no whitespace errors.
- `Phase1MigrationTests` builds the pre-Phase1 schema, seeds baseline users/roles/leads/projects/tasks and a role restriction using only old columns, then upgrades. Existing numeric IDs, Won status, role restriction and populated rowversions are verified.
- Windows file-lock limitation: backend builds during a live Playwright server can lock the Debug executable. SQL verification uses Release while the browser server runs Debug. No test failure is hidden by this separation.

## GitHub CI recovery and scoped UI checkpoint — 20 October 2026

- Prior `39bce36` GitHub SQL/API run failed one test out of 36: `Phase1LoginApiTests.Missing_account_attempts_are_limited_across_spoofed_forwarded_addresses`. The browser CI environment set `RateLimiting__LoginRequestsPerMinute=30` while the security test assumed the default account limit of five.
- Commit `c2cd4a0` sets `LoginAccountRequestsPerMinute=5` and `LoginIpRequestsPerMinute=300` inside the integration test host only, isolating tests from the browser runner configuration. Production login throttling was **not changed**.
- GitHub [CI run 39382007965](https://github.com/dharmendra1031/BusinessFlow/actions/runs/39382007965) passed **36/36 SQL/API tests** and **32/32 Chromium tests**; Windows [IIS packaging run 39382007789](https://github.com/dharmendra1031/BusinessFlow/actions/runs/39382007789) passed.
- Commit `fa497af` aligns Team navigation and lead Create/Edit/Assign/Status and follow-up controls with effective permissions instead of hard-coded Manager/Admin-only UI rules. GitHub [CI run 39383136109](https://github.com/dharmendra1031/BusinessFlow/actions/runs/39383136109) passed.
- Commit `ff61165` adds browser regression cases for restricted-employee read-only leads and a custom-role `employees.view` grant. GitHub [CI run 39383449392](https://github.com/dharmendra1031/BusinessFlow/actions/runs/39383449392) passed **36/36 SQL/API tests** and **34/34 Chromium tests**; [IIS packaging run 39383449383](https://github.com/dharmendra1031/BusinessFlow/actions/runs/39383449383) passed.
- Remaining Phase 1: permission-driven Project/Task/Bug/Worklog UI capabilities, large assignment lookup selection beyond initial 200 records, final review of scope revocation and multi-role interactions, staging migration/recovery evidence and release sign-off.
- Evidence above reflects isolated CI environments. **No live production deployment, 100–200-user load capability or final Phase 1 production acceptance is asserted.**
