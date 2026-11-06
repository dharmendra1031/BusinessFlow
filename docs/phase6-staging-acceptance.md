# Phase 6 — staging acceptance checklist

This is a manual, authorized deployment procedure. Passing GitHub CI and a generated IIS package **does not mean the staging database or IIS site was deployed**.

## 1. Freeze release identity

- Use one exact `development` commit SHA for the IIS ZIP and matching EF migration bundle.
- Download the GitHub Action `staging-iis-package` artifact and keep its `release-manifest.json`.
- Verify ZIP and bundle SHA-256 against the manifest before transferring files to the approved staging host.
- Preserve the currently deployed IIS package, configuration and rollback owner. Do not place credentials or customer records in GitHub or chat.

## 2. Isolated SQL database and restore

Use the established [Phase 1 recovery checklist](phase1-staging-recovery-checklist.md), starting with its **read-only inventory** and `scripts/check-local-staging.ps1`. Confirm the source database and the proposed target `LeadManagement_Stage_YYYYMMDD` are different, and the target does **not** already exist.

With DBA authorization, take a COPY_ONLY, CHECKSUM backup, use RESTORE VERIFYONLY, inspect logical files, and restore into a **new isolated database** without `WITH REPLACE`. Protect any sensitive backup and confirm `DBCC CHECKDB`, pre-migration counts, migration history, representative numeric IDs, role restrictions and data integrity.

## 3. Apply the matching migration bundle to staging only

Run the matching `efbundle.exe` from the same release under a **separate migration identity**, with an explicit staging database connection. Never run it against the source or production database.

After a successful migration, run the new **read-only Phase 6** validation using integrated SQL Server authentication:

```powershell
.\scripts\verify-billing-staging.ps1 -Server 'STAGING_SQL_SERVER' -Database 'LeadManagement_Stage_YYYYMMDD'
```

It checks the applied `20261105120000_AddFinancialAdjustmentApprovals` migration, SQL tables, numeric BIGINT IDENTITY keys, rowversion columns, unique receipt/refund indexes, pending approval uniqueness, financial check constraints and **record counts only**. It does not execute updates, create/restore databases, or expose customer financial details. Retain redacted result and verify that migration is idempotent.

## 4. Separate HTTPS/IIS staging site

Deploy the matching IIS ZIP to a **distinct staging site and application pool**; preserve IIS production settings. Connect the app only to the restored staging database with a least-privilege application account, not the schema-migration identity. Never run demos or synthetic seed scripts on a client database; the IIS package intentionally excludes the old `seed_kuwait_demo.sql`.

With a dedicated **staging Admin** account, run:

```powershell
$admin = Get-Credential
.\scripts\verify-release.ps1 -BaseUrl 'https://STAGING_DOMAIN' -Credential $admin -VerifyBilling
```

This checks HTTPS/response headers/readiness, cookie login and CSRF/logout, then performs **read-only** queries for invoices, pending financial approvals, outstanding, collections and customer search. It cannot simulate end-to-end finance writes.

## 5. Authorized UAT and rollback acceptance

Use **sanitized test records only**, a dedicated staging database and two different Admin logins. Have QA and finance reviewers verify:

- Signed Contract → advance/milestone Invoice → partial/full receipt; no overbilling or overpayment.
- Credit Note request stays Pending without changing financial balances; maker cannot self-approve; independent reviewer approves/rejects with evidence.
- Refund requests require original payment and unique evidence reference; enforce partial refund and final settlement limits.
- Stale invoice and stale approval version conflicts, duplicates, concurrent processing and audit trail.
- Currency-specific customer ledger, outstanding and gross receipts/refunds/net collections.
- A4 invoice and quotation PDFs; **commercial copies are not GST/VAT statutory tax invoices**.
- All roles denied outside their approved scope; HTTPS logs and IIS error checks.

Record DBA, QA and business sign-off; backup and restored database hashes, applied migrations, counts, recovery time, rollback method and approved previous package. Rehearse rolling back the IIS package **and restoring a separate pre-migration backup** if the migration is not backward-compatible. **Do not automatically downgrade production SQL** or drop any existing database.

## Scope and status

- CI validates synthetic SQL, automated tests, PowerShell syntax and release ZIP contents.
- Real isolated restore, SQL verification on staging, HTTPS/IIS deployment, finance UAT, RTO/RPO, rollback rehearsal and formal sign-off remain **pending manual execution**.
- Tax invoice jurisdiction, seller tax-registration profile, statutory numbering, tax breakdown and local fiscal compliance remain outside this increment.
