# Sales Quotation — Commercial Workflow

BusinessFlow quotation lifecycle on `development`. Existing Lead `ProjectAmount` and `Proposal` lead-status are pipeline data, not quotation or revenue records; they were not duplicated or changed.

## Workflow

1. Admin selects an existing Lead and creates one quotation. SQL has a unique `LeadId` index; duplicate requests return HTTP 409.
2. Draft can contain up to 100 items, quantity, unit price, discount percentage, and tax percentage. The API calculates and snapshots three-decimal line amounts and the quotation totals. Existing amount-only revisions remain readable.
3. Every revision is immutable and has its own ID. Revisions of a sent or approved quotation reset the status to Draft and invalidate prior approval.
4. Admin submits the Draft for internal review (`PendingApproval`). Admin records a written approval (`Approved`) or returns it to Draft with a reason. Review actions are audited. **This phase does not enforce independent two-person approval**; roles remain Admin-only.
5. Approved quotations can be marked Sent. Client acceptance/rejection may only be recorded after Sent and requires a named client contact and a reference to external evidence. Recording acceptance is **not a digital client signature**.
6. Accepted quotation may create exactly one Contract Draft, linked to the accepted revision. Signed status requires signer, date, and documentary evidence reference. Original contract is retained; these fields record an external signature, not execute one online.
7. Quotation PDFs use the existing Windows RDLC renderer; each archived revision prints its original financial snapshot and is labeled Superseded if it is not the latest revision. Generated PDF is not an invoice.

## Technical rules

- All new entity primary keys are SQL Server `BIGINT IDENTITY`. No application UUID/GUID identifiers.
- Status changes, approval decisions, contract creation and signing are audited and require current SQL `rowversion` values.
- Money is `decimal(18,3)`, quantity `decimal(12,3)`; per-line discount and tax rates `decimal(5,2)`. All rounding is to three decimals, midpoint away from zero.
- Tax fields represent a configured percentage, **not** a jurisdiction-validated GST/VAT calculation. Tax law compliance, jurisdiction rules, invoice tax documents, payment collection and accounting remain separate.
- Only Admin currently accesses commercial records. No provider integrations, automatic signing, client portal or external authorization claims.
- Additive migrations only, verify on a staging copy after backup before production.

## Endpoints

- `GET/POST /api/admin/quotations`; `GET /api/admin/quotations/{id}`
- `POST /api/admin/quotations/{id}/revisions`
- `POST /api/admin/quotations/{id}/submit`; `POST /api/admin/quotations/{id}/review`
- `POST /api/admin/quotations/{id}/status`
- `GET /api/admin/quotations/{id}/pdf?revision=N`
- `POST /api/admin/quotations/{id}/contract`; `POST /api/admin/quotations/{id}/contract/sign`

Remaining higher-level business work: invoice/payments, jurisdiction-specific tax rules, contract legal review, independent maker-checker controls, external client approval and electronic signature.
