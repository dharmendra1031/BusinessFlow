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

Phase 1 remains in progress. Rowversions/conflict UI, terminal-status approvals, account-aware login throttling, employee directory pagination, full action-capability UI, expanded permission coverage and existing-data upgrade acceptance remain pending. The existing Serializable worklog transaction still needs the planned parallel-post verification. This checkpoint is not Phase 1 completion or production acceptance.
