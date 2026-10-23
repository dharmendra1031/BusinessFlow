# Phase 2 — Duplicate Lead Review Master

**Application commit:** `09b2a4d` on `development`. **Status:** CI verified; local staging migration and production acceptance pending.

## Delivered

- Admin-only "Duplicate review" screen with paginated Pending and Reviewed lists.
- Possible duplicate pairs are matched on indexed, persisted SQL Server keys:
  - Phone: remove spaces, tab, plus signs, hyphens, brackets and dots; match if at least seven digits remain.
  - Email: trim surrounding spaces and compare case-insensitively.
- If both phone and email match, the pair appears only once.
- Admin records an immutable `Confirmed` or `NotDuplicate` decision with a mandatory review note, actor identity and timestamp.
- Pair ordering and a unique SQL index protect from duplicate decisions. Invalid pairs and unauthorized Employee access are rejected.
- Leads, source, status, ownership, follow-ups, projects and original BIGINT IDs are **not** automatically merged, deleted or reassigned.
- New EF migration `20261023121500_AddLeadDuplicateReviews` adds computed search columns and an auditable review table, with existing lead records preserved.
- No provider credentials or integrations were introduced.

## Verified CI evidence

- [CRM verification run 39898928500](https://github.com/dharmendra1031/BusinessFlow/actions/runs/39898928500) — **43/43 SQL/API** and **39/39 Playwright** tests PASS; migration snapshot and frontend production build PASS.
- [Windows IIS package run 39898928513](https://github.com/dharmendra1031/BusinessFlow/actions/runs/39898928513) — PASS.
- Dedicated SQL-backed `Phase2DuplicateLeadTests` cover formatting/case matches, Admin-only access, invalid decisions, reviewed/pending state, audit history and old schema migration preserving lead IDs.
- Existing 39 browser regression tests pass; a *dedicated interactive browser test for the new review screen* is still recommended. Do not infer it from the generic existing browser suite.

## Important limitations

- Phone matching is **format-normalization**, not international E.164 parsing. Country-prefix ambiguities, extensions or alternative phone formats require careful manual review; a match is a suggestion, not proof of identity.
- Manual decisions remain in history, but full record merging, reopening/revising decisions, workflow notifications, robust large-dataset performance and upstream-provider deduplication are not implemented in this milestone.
- Admin-only cross-company CRM review is appropriate for current single-organization data. For multi-tenant deployments, tenant isolation will need explicit test coverage.
- Phase 1 local staging on the user's LocalDB instance had five applied migrations and 24 leads at last observed preflight. No live SQL changes were executed here. **Back up and restore to a separate staging database before running pending migrations.**

## Next

Phase 2: lead capture ingestion design and validation with secure provider webhooks and idempotency; don't enable Facebook/WhatsApp until approved account credentials, signatures, consent and data-handling controls are available.
