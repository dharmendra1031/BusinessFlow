# Phase 2 — Automatic Lead Routing Master

**Verified application commit:** `6fb3873`, branch `development`. **CI Verified; local staging and production acceptance pending.**

## Implemented first milestone

- Admin-only Lead Routing Master accessible in the existing BusinessFlow sidebar, with source, target team, maximum open leads per employee and Enable/Disable controls.
- Numeric BIGINT auto-increment rule IDs, one optional routing rule per source, with audited changes.
- **First enabled integration: signed Website intake (source ID 2) only.** Rules for other sources cannot currently be activated. No rule or a disabled rule keeps the configured Website intake owner.
- An enabled rule chooses an active employee in the selected active team and active department, using **fewest open Leads** (statuses 1, 2, 3, 4, 7). Equal workloads are broken deterministically by employee ID.
- The `MaxOpenLeads` cap is enforced from the current SQL database counts. If there is no eligible employee with capacity, intake responds `503` and does not save a partial Lead, submission receipt or assignment. A website backend can retry the same signed event after capacity is restored.
- On successful routing, the Lead, customer code, original `CreatedById`, `LeadAssignment` history, `AuditLog` entry and website idempotency receipt commit together.
- Existing CRM manual lead assignment, existing Lead owners, follow-ups, duplicate-review decisions, project records and UI theme are unaffected.
- Database migration `20261023180000_AddLeadRoutingRules` creates the routing rules table; no existing Lead IDs or records are modified. Rules start unconfigured.

## Verification evidence

- [CRM Verify run 39931708025](https://github.com/dharmendra1031/BusinessFlow/actions/runs/39931708025): **47/47 SQL/API tests PASS**, **40/40 Playwright tests PASS**, EF migration snapshot, .NET dependency audit and React production build PASS.
- [IIS package run 39931708080](https://github.com/dharmendra1031/BusinessFlow/actions/runs/39931708080): **PASS**.
- SQL tests verify Admin-only settings, validation of inactive teams/capacity, least-workload selection, capacity exhaustion without partial records, assignment audit/history, and fallback to original owner after disabling a rule.
- The browser regression includes an Admin master form flow to create and enable a Website team routing rule. This uses mocked route responses, not live performance testing.

## Open acceptance gates

- **Local SQL Server staging and migration:** the user's LocalDB source `LeadManagement` was last read with 24 Leads and five historic migrations. Backup/restore into an isolated clone and migrate the clone before running migration against the source.
- **Rules beyond Website source**, geographic territory logic, manager manual override at the policy level, load balancing across multiple application instances, distributed routing fairness, deadlock/retry under simultaneous high-volume submissions, and enterprise load testing are pending.
- Website owner must be an active user; only active team members in an active department are routing candidates. Broader role-grant eligibility and manager-policy rules need explicit future design and acceptance.
- External Website integration, Meta/WhatsApp provider sandbox tests, HTTPS/IIS staging and production rollout remain pending.

**Phase 2 is not production accepted.** The implementation is a verified first routing master milestone.
