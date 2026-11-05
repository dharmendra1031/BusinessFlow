# Financial adjustment approvals (maker-checker)

## Required flow

The existing credit-note and refund endpoints now **submit** a Pending request (HTTP 202); they no longer post financial changes immediately. An independent Admin reviews the request in the same Billing screen. The maker cannot decide their own request, even when they hold the Admin role.

- `POST /api/admin/billing/invoices/{invoiceId}/credits`: submit credit request.
- `POST /api/admin/billing/invoices/{invoiceId}/refunds`: submit refund request with original payment and external evidence reference.
- `GET /api/admin/billing/adjustments?state=Pending`: review queue.
- `POST /api/admin/billing/adjustments/{id}/decision`: accept or reject, with reviewer comment and current rowversion.
- `GET /api/admin/billing/adjustments/invoices/{invoiceId}`: request history.

The approval request captures the invoice rowversion. If another payment/adjustment changes the invoice while pending, approval is **blocked**; the reviewer should reject the stale request and have the maker resubmit. SQL transactions and rowversion prevent conflicting approval writes. Database keeps requestor, reviewer, dates, evidence and audit.

## Financial posting

Approved credits reduce `CreditedAmount` and contract billed balance and create a historical `SalesCreditNote`. Approved refunds reduce invoice net collected and increase recorded refunded totals, keeping original payment records unchanged.

A Pending/Rejected approval **never changes** invoice totals or contract balances. No external money transfer is initiated. Financial approvals are Admin-only, with two distinct Admin user IDs required. For a one-Admin installation, credit/refund adjustments cannot proceed until a second Admin is configured.

## Compliance and deployment

This is internal financial control, not a legal GST/VAT invoice, bank integration, e-signature or external payment verification. Existing invoices, customer records, reports and receipt IDs remain numeric. The new primary key is `BIGINT IDENTITY`; migration is additive. Run SQL migrations on staging after backup and verify rollback before deployment. The two POST adjustment endpoints intentionally change from immediate execution to submission; update any external integrations accordingly.
