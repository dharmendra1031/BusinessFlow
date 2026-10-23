# Phase 2 — Lead Source Master checkpoint

**Application commit:** `67bb3b1` on `development`. **Status:** CI verified, staging migration and production acceptance pending.

## Delivered

- New Admin-only Lead Source Master screen for creating, renaming, deactivating and reactivating sources.
- Dedicated admin API with validation, duplicate-name checking, concurrency-safe SQL unique index and audit history.
- SQL Server EF migration `20261023090000_AddLeadSourceManagement` introduces `IsActive` with existing sources enabled and a source-name uniqueness index; source IDs remain BIGINT identities.
- New leads can only select active sources. Existing leads keep their source ID and source name after a source is renamed or deactivated. Existing leads may still be edited while retaining the inactive source.
- Original sidebar theme and other CRM modules remain unchanged.

## CI evidence

- [GitHub CRM CI run](https://github.com/dharmendra1031/BusinessFlow/actions/runs/39887789336): **41/41 SQL/API tests PASS**, **39/39 Playwright tests PASS**, frontend production build and migration snapshot check PASS.
- [Windows IIS packaging](https://github.com/dharmendra1031/BusinessFlow/actions/runs/39887789356): PASS.
- API tests cover Admin/Employee authorization, duplicate and invalid names, historical lead preservation, inactive-source rejection on new leads, reactivation, audit records and upgrade migration preserving source BIGINT IDs.
- Browser test covers Admin source create/rename/deactivate and exclusion of inactive sources in the new-lead form.

## Deployment gate

The user's local `LeadManagement` database was last observed at five applied Phase 1 migrations with 24 leads. **No Phase 1 or Phase 2 schema change was performed on that local database through this task.** Before applying any new migration: complete the approved isolated backup/restore rehearsal, inspect existing EF history, apply migrations to the isolated clone, validate all 24 leads and verify rollback. No production readiness claim is made.

## Next Phase 2 item

Duplicate lead review: normalized phone/email checks, explicit decision workflow, retention of history and clear authorization. No automated destructive merge without an approved business rule.
