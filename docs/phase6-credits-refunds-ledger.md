# Billing adjustments and customer ledger

## Credit notes
- Admin can issue an immutable, numeric-ID credit note against a non-void commercial invoice.
- Original invoice value and payment receipts are not overwritten.
- `CreditedAmount` is cumulative and cannot exceed the original invoiced amount.
- Credit reduces the signed contract's net billed amount, restoring the unbilled contract value. SQL transactions and rowversion checks protect concurrent writes.
- `NetAmount = Amount - CreditedAmount`; `Outstanding = max(0, NetAmount - PaidAmount)`; `RefundDue = max(0, PaidAmount - NetAmount)`.
- If collected funds exceed the reduced invoice net amount, invoice becomes `RefundDue`; no further payments are accepted while this is outstanding.
- A fully credited invoice with no net collection becomes `Credited`. A settled net invoice becomes `Paid`.

## Refunds
- Record only actual completed repayment against a prior payment of that same invoice.
- Each refund has a numeric auto-increment ID, source payment ID, method, external reference, reason, refund date, staff user ID and creation timestamp.
- A refund cannot exceed the invoice's current refundable balance or the original payment's unrefunded amount.
- Both invoice and original payment maintain cumulative refunded amounts. The invoice's net collected amount decreases by refund; original receipt remains immutable.
- External refund reference is unique. Refunds and credit notes are audited.
- Current workflow is an **administrative record of an external refund**; it does not initiate money transfers, bank transactions or payment gateway actions.

## Customer-wise ledger
- Search existing Customer Master, then retrieve invoices attached through `Customer → Lead → Quotation → Contract → Invoice`.
- Currency-specific totals include net invoiced, net collected, credited, refunded, unpaid balance and refund due. Never add distinct currency amounts.
- Invoice links expose payment, credit and refund histories. Historical leads not linked to a Customer Master are not included in the customer ledger.
- Admin only. Independent approval and credit-note PDF numbering/legal tax-compliance remain separate work.

## Deployment note
- New SQL Server migration `20261105110000_AddSalesCreditsAndRefunds` is additive.
- Verify EF snapshot consistency, SQL integration and browser tests.
- Before production: database backup, staging migration and rollback rehearsal.
- This is **commercial recordkeeping, not statutory GST/VAT tax invoicing**.
