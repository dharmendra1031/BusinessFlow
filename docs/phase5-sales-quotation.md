# Sales Quotation v1

First commercial increment on `development`. No existing quotation/proposal table, API or screen was found in the repository before implementation. The existing Lead `ProjectAmount` and the `Proposal` lead status are not quotations and are unchanged.

- Admin-only quotation linked to a **single existing Lead**. One quotation per lead, with unlimited immutable revisions. The unique SQL Server `LeadId` index prevents accidental duplicate creation; HTTP returns 409.
- Amount is `decimal(18,3)`, strictly positive with up to 3 decimals; currency is a 3-letter code stored uppercase, not hardcoded to KWD or INR.
- Status moves `Draft → Sent → Accepted/Rejected`. Creating a revision on a Draft/Sent quotation returns it to Draft; terminal states remain locked.
- All changes are audited. Revision records are never edited or deleted. SQL `rowversion` protects conflicting revision/status writes.
- Numeric `BIGINT IDENTITY` primary keys; no UUID/GUID IDs. Existing Lead/Customer/Project records remain unchanged.
- API: `GET/POST /api/admin/quotations`, `GET /{id}`, `POST /{id}/revisions`, `POST /{id}/status`.
- This is **internal commercial tracking**, not client approval proof or proof of delivery. The Sent/Accepted/Rejected status is recorded manually by Admin.
- Pending: detailed line items/taxes/discounts, formal approval workflows, PDF quotation, client response evidence, contracts, invoices, payment collection, and fine-grained sales permissions.

Run the additive EF Core migration on a backed-up staging copy and complete role/API/browser regression before production use.
