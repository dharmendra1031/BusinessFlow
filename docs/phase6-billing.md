# Phase 6: Invoice & Collections

Commercial billing uses signed contracts only. There is no duplicate Customer, Lead or Contract master.

- `BIGINT IDENTITY` invoice and payment primary keys.
- Advance, Milestone, Final invoices draw down the signed contract's unbilled value using SQL Server `rowversion` on the contract. Overbilling and stale-write attempts are rejected.
- Recorded payments draw down an invoice outstanding amount under the invoice `rowversion`. One bank/payment reference can only be recorded once. Overpayment is rejected.
- Unpaid and uncollected invoices can be voided with an audit reason; the contract's unbilled value is restored. Paid invoices cannot be voided here (refund/credit note workflow is separate).
- The contract ledger distinguishes **unbilled signed contract value** from **issued invoice outstanding** and **collected funds**. They must never be added into one "outstanding" number.
- Outstanding report: `GET /api/admin/billing/reports/outstanding` supports search, ISO currency, overdueOnly, asOf date, page/pageSize. Summaries are grouped by currency, never converted or combined.
- Collection report: `GET /api/admin/billing/reports/collections` supports search, ISO currency, from/to dates (up to 366 days) and pagination. Dates refer to **payment received date**, not invoice issue date.
- Commercial invoice PDF: `GET /api/admin/billing/invoices/{id}/pdf` uses the existing Windows RDLC PDF renderer. It displays the immutable billed amount plus the **current** payment/void status and outstanding at render time. If a payment is entered later, a freshly generated copy reflects the new balance.
- Financial pages and APIs are **Admin-only** until commercial permissions are explicitly approved.

**Important:** A service/commercial invoice PDF here is **not a jurisdiction-validated tax invoice**. Tax registration, tax calculation, statutory invoice numbering, legal seller identity/address and fiscal compliance requirements need their own verified implementation before issuing legally compliant GST/VAT invoices. There is no external payment gateway or bank reconciliation.

Production acceptance still requires deployment to a backed-up staging copy, restore/rollback rehearsal, permission review and business UAT.
